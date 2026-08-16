using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace DSPCalculatorPlus
{
    /// <summary>
    /// Group A - belt / sorter tier override (Phase 2).
    ///
    /// Wraps <c>DSPCalculator.Bp.BpConnector.GenerateFullBlueprint</c> (the
    /// single entry point for one blueprint generation). In the Prefix it
    /// snapshots the solution's <c>beltsAvailable</c> / <c>sortersAvailable</c>
    /// candidate lists and replaces each with a filtered copy containing only
    /// the user's forced tier; the Postfix restores the originals
    /// unconditionally. Every downstream tier pick in DSPCalculator
    /// (CalcItemSumInfos, BpBlockProcessor, BpProcessor) reads those two
    /// lists, so a single wrap forces the tier uniformly - no need to chase
    /// the inlined pick sites. This mirrors the workspace's
    /// snapshot-in-Prefix / restore-in-Postfix pattern.
    ///
    /// Semantics: forcing a tier keeps ONLY that tier as a candidate, so the
    /// generator uses it exactly. If a single belt of that tier can't carry
    /// an item's flow, DSPCalculator's own overflow check (which compares
    /// against <c>beltsAvailable.Last()</c>, now the forced tier) fires -
    /// which is where the Phase 3 multi-lane fix takes over.
    ///
    /// Everything is reached by reflection - there is no compile-time
    /// reference to DSPCalculator. If any target member is missing (a
    /// DSPCalculator version this mod wasn't verified against), the patch is
    /// not installed and DSPCalculator's stock behaviour is left untouched.
    /// </summary>
    internal static class TierOverridePatch
    {
        // Resolved once in Apply(); null until then.
        private static FieldInfo _fSolution;          // BpConnector.solution
        private static FieldInfo _fBeltsAvailable;    // SolutionTree.beltsAvailable
        private static FieldInfo _fSortersAvailable;  // SolutionTree.sortersAvailable
        private static FieldInfo _fBeltItemId;        // BpBeltInfo.itemId
        private static FieldInfo _fSorterGrade;       // BpSorterInfo.grade
        private static FieldInfo _fBeltsAscending;    // static BpDB.beltsAscending
        private static bool _ready;

        // Vanilla item IDs - confirmed against the live game (StackingPlus's own
        // belt-proto log lines, and the "Pile Sorter (id 2014)" entries in the
        // pole diagnostic's worst-consumer log), not guessed. StackingPlus raises
        // the STACKING count on these same items, it never adds new tier IDs, so
        // this list needs no update for that compat.
        private const int BeltMk1Id = 2001, BeltMk2Id = 2002, BeltMk3Id = 2003;
        private const int SorterMk1Id = 2011, SorterMk2Id = 2012, SorterMk3Id = 2013, SorterMk4Id = 2014;

        // DSPCalculator always hands the paster a blueprint saved under this
        // temp filename - mirrors PowerPolePatch's own marker/dedup so this
        // diagnostic never touches a normal manual paste and never double-logs
        // the same generated blueprint.
        private const string DspCalcMarker = "DSPCalcBPTemp";
        private static readonly ConditionalWeakTable<BlueprintData, object> _diagProcessed =
            new ConditionalWeakTable<BlueprintData, object>();
        private static readonly object DiagMarker = new object();

        // Set by Prefix (the pass right before generation), read by DiagPastePrefix
        // (the pass at paste time, after generation) so the end-to-end diagnostic
        // knows WHY the final blueprint doesn't match a requested tier: a genuine
        // mismatch (something actually wrong) vs a requested tier that was
        // correctly unavailable (locked/not unlocked) and safely fell back to
        // Auto - the two look identical from the final building counts alone, but
        // only one is a bug. Reset at the start of every Prefix call so a stale
        // value from an earlier generation can never leak into a later one's diag.
        private static bool _lastBeltFellBack;
        private static bool _lastSorterFellBack;

        /// <summary>
        /// Resolves the DSPCalculator targets and installs the patch. Safe to
        /// call once from Plugin.Awake (DSPCalculator is a hard dependency, so
        /// it is already loaded). No-op with a warning if anything is missing.
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            var tConnector = AccessTools.TypeByName("DSPCalculator.Bp.BpConnector");
            if (tConnector == null)
            {
                DSPCalculatorPlusLog.Warn("[tier] BpConnector not found - tier override disabled (DSPCalculator missing or renamed).");
                return;
            }

            var target = AccessTools.Method(tConnector, "GenerateFullBlueprint");
            if (target == null)
            {
                DSPCalculatorPlusLog.Warn("[tier] BpConnector.GenerateFullBlueprint not found - tier override disabled.");
                return;
            }

            var tBeltInfo = AccessTools.TypeByName("DSPCalculator.Bp.BpBeltInfo");
            var tSorterInfo = AccessTools.TypeByName("DSPCalculator.Bp.BpSorterInfo");
            var tBpDB = AccessTools.TypeByName("DSPCalculator.Bp.BpDB");

            _fSolution = AccessTools.Field(tConnector, "solution");
            var tSolution = _fSolution?.FieldType;
            _fBeltsAvailable = tSolution != null ? AccessTools.Field(tSolution, "beltsAvailable") : null;
            _fSortersAvailable = tSolution != null ? AccessTools.Field(tSolution, "sortersAvailable") : null;
            _fBeltItemId = tBeltInfo != null ? AccessTools.Field(tBeltInfo, "itemId") : null;
            _fSorterGrade = tSorterInfo != null ? AccessTools.Field(tSorterInfo, "grade") : null;
            _fBeltsAscending = tBpDB != null ? AccessTools.Field(tBpDB, "beltsAscending") : null;

            if (_fSolution == null || _fBeltsAvailable == null || _fSortersAvailable == null
                || _fBeltItemId == null || _fSorterGrade == null || _fBeltsAscending == null)
            {
                DSPCalculatorPlusLog.Error("[tier] one or more DSPCalculator members not found - tier override disabled. "
                    + "solution=" + (_fSolution != null) + " belts=" + (_fBeltsAvailable != null)
                    + " sorters=" + (_fSortersAvailable != null) + " beltItemId=" + (_fBeltItemId != null)
                    + " sorterGrade=" + (_fSorterGrade != null) + " beltsAscending=" + (_fBeltsAscending != null));
                return;
            }

            _ready = true;
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(TierOverridePatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(TierOverridePatch), nameof(Postfix)) { priority = Priority.VeryHigh });
            DSPCalculatorPlusLog.Info("[tier] Group A patch installed on BpConnector.GenerateFullBlueprint.");

            var pasteTarget = AccessTools.Method(typeof(PlayerController), "OpenBlueprintPasteMode",
                new[] { typeof(BlueprintData), typeof(string), typeof(bool) });
            if (pasteTarget != null)
            {
                harmony.Patch(pasteTarget, prefix: new HarmonyMethod(typeof(TierOverridePatch), nameof(DiagPastePrefix)));
                DSPCalculatorPlusLog.Info("[tier] diagnostic installed on PlayerController.OpenBlueprintPasteMode "
                    + "(logs the actual belt/sorter tier distribution in every generated blueprint).");
            }
        }

        /// <summary>
        /// Read-only: counts the actual belt/sorter tiers present in the final
        /// pasted blueprint and compares against the configured override, so a
        /// forced tier can be confirmed end-to-end instead of trusting that the
        /// candidate-list filter in Prefix/Postfix above propagated correctly.
        /// Mirrors PowerPolePatch's own marker filter + dedup so this never
        /// touches a normal manual paste or double-logs one generated blueprint.
        /// </summary>
        private static void DiagPastePrefix(BlueprintData blueprint, string fullPath)
        {
            try
            {
                if (blueprint == null || blueprint.buildings == null || blueprint.buildings.Length == 0) return;
                if (string.IsNullOrEmpty(fullPath) || fullPath.IndexOf(DspCalcMarker, StringComparison.OrdinalIgnoreCase) < 0) return;
                object existing;
                if (_diagProcessed.TryGetValue(blueprint, out existing)) return;
                _diagProcessed.Add(blueprint, DiagMarker);

                int b1 = 0, b2 = 0, b3 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
                foreach (BlueprintBuilding building in blueprint.buildings)
                {
                    switch (building.itemId)
                    {
                        case BeltMk1Id: b1++; break;
                        case BeltMk2Id: b2++; break;
                        case BeltMk3Id: b3++; break;
                        case SorterMk1Id: s1++; break;
                        case SorterMk2Id: s2++; break;
                        case SorterMk3Id: s3++; break;
                        case SorterMk4Id: s4++; break;
                    }
                }

                var cfg = Plugin.Config;
                BeltTier beltTier = cfg.BeltTierOverride.Value;
                SorterTier sorterTier = cfg.SorterTierOverride.Value;

                DSPCalculatorPlusLog.Info("[tier][diag] config: BeltTierOverride=" + beltTier
                    + (_lastBeltFellBack ? " (UNAVAILABLE for this blueprint - correctly fell back to Auto instead of forcing a locked/missing tier)" : "")
                    + " SorterTierOverride=" + sorterTier
                    + (_lastSorterFellBack ? " (UNAVAILABLE for this blueprint - correctly fell back to Auto)" : "")
                    + ". Final blueprint belts: Mk1=" + b1 + " Mk2=" + b2 + " Mk3=" + b3
                    + "; sorters: Mk1=" + s1 + " Mk2=" + s2 + " Mk3=" + s3 + " Mk4(pile)=" + s4 + ".");

                // Only a genuine mismatch is worth a warning - not the case where
                // Prefix already correctly detected the tier was unavailable and
                // deliberately left it on Auto (that is respecting tech-unlock
                // status working as intended, not a bug).
                if (beltTier != BeltTier.Auto && !_lastBeltFellBack)
                {
                    bool otherBeltsPresent = (beltTier != BeltTier.Mk1 && b1 > 0)
                        || (beltTier != BeltTier.Mk2 && b2 > 0)
                        || (beltTier != BeltTier.Mk3 && b3 > 0);
                    if (otherBeltsPresent)
                        DSPCalculatorPlusLog.Warn("[tier][diag] BeltTierOverride=" + beltTier + " but belts of OTHER tiers are present in "
                            + "the final blueprint - the override did not fully apply. Report this line.");
                }
                if (sorterTier != SorterTier.Auto && !_lastSorterFellBack)
                {
                    bool otherSortersPresent = (sorterTier != SorterTier.Mk1 && s1 > 0)
                        || (sorterTier != SorterTier.Mk2 && s2 > 0)
                        || (sorterTier != SorterTier.Mk3 && s3 > 0)
                        || (sorterTier != SorterTier.Mk4 && s4 > 0);
                    if (otherSortersPresent)
                        DSPCalculatorPlusLog.Warn("[tier][diag] SorterTierOverride=" + sorterTier + " but sorters of OTHER tiers are "
                            + "present in the final blueprint - the override did not fully apply. Report this line.");
                }
            }
            catch (Exception ex)
            {
                DSPCalculatorPlusLog.Error("[tier][diag] tier-distribution diagnostic failed (harmless, informational only): " + ex);
            }
        }

        // __state (when non-null) = { solution, originalBelts-or-null, originalSorters-or-null }.
        private static void Prefix(object __instance, out object[] __state)
        {
            __state = null;
            if (!_ready || __instance == null) return;

            var cfg = Plugin.Config;
            BeltTier beltTier = cfg.BeltTierOverride.Value;
            SorterTier sorterTier = cfg.SorterTierOverride.Value;
            _lastBeltFellBack = false;
            _lastSorterFellBack = false;
            if (beltTier == BeltTier.Auto && sorterTier == SorterTier.Auto) return;

            object solution = _fSolution.GetValue(__instance);
            if (solution == null) return;

            object originalBelts = null;
            object originalSorters = null;

            if (beltTier != BeltTier.Auto)
            {
                var current = _fBeltsAvailable.GetValue(solution) as IList;
                var filtered = FilterBeltsToTier(current, (int)beltTier);
                if (filtered != null)
                {
                    originalBelts = current;
                    _fBeltsAvailable.SetValue(solution, filtered);
                }
                else _lastBeltFellBack = true;
            }

            if (sorterTier != SorterTier.Auto)
            {
                var current = _fSortersAvailable.GetValue(solution) as IList;
                var filtered = FilterSortersToGrade(current, (int)sorterTier);
                if (filtered != null)
                {
                    originalSorters = current;
                    _fSortersAvailable.SetValue(solution, filtered);
                }
                else _lastSorterFellBack = true;
            }

            if (originalBelts != null || originalSorters != null)
                __state = new object[] { solution, originalBelts, originalSorters };
        }

        private static void Postfix(object[] __state)
        {
            if (__state == null) return;
            object solution = __state[0];
            object originalBelts = __state[1];
            object originalSorters = __state[2];
            if (originalBelts != null) _fBeltsAvailable.SetValue(solution, originalBelts);
            if (originalSorters != null) _fSortersAvailable.SetValue(solution, originalSorters);
        }

        /// <summary>
        /// Returns a new list (same runtime type as <paramref name="current"/>)
        /// keeping only the belt whose tier == MkN, mapped via
        /// BpDB.beltsAscending[N-1]. Returns null (leave Auto) if the tier is
        /// out of range or not present in the currently-available belts (e.g.
        /// locked under DSPCalculator's tech limit).
        /// </summary>
        private static IList FilterBeltsToTier(IList current, int tier)
        {
            if (current == null || current.Count == 0) return null;

            var ascending = _fBeltsAscending.GetValue(null) as IList;
            int idx = tier - 1;
            if (ascending == null || idx < 0 || idx >= ascending.Count)
            {
                DSPCalculatorPlusLog.Warn("[tier] belt Mk" + tier + " is out of range (only "
                    + (ascending == null ? 0 : ascending.Count) + " belt tiers exist) - leaving belt tier on Auto.");
                return null;
            }

            int forcedItemId = (int)_fBeltItemId.GetValue(ascending[idx]);
            var filtered = (IList)Activator.CreateInstance(current.GetType());
            foreach (var b in current)
            {
                if ((int)_fBeltItemId.GetValue(b) == forcedItemId)
                    filtered.Add(b);
            }

            if (filtered.Count == 0)
            {
                DSPCalculatorPlusLog.Warn("[tier] belt Mk" + tier + " (itemId " + forcedItemId
                    + ") is not in the available belts (locked under tech limit?) - leaving belt tier on Auto.");
                return null;
            }

            DSPCalculatorPlusLog.Info("[tier] belt forced to Mk" + tier + " (itemId " + forcedItemId + ").");
            return filtered;
        }

        /// <summary>
        /// Returns a new list (same runtime type as <paramref name="current"/>)
        /// keeping only sorters whose grade == N (grade &gt;= 4 for Mk4 /
        /// pile). Returns null (leave Auto) if no such sorter is available.
        /// </summary>
        private static IList FilterSortersToGrade(IList current, int tier)
        {
            if (current == null || current.Count == 0) return null;

            var filtered = (IList)Activator.CreateInstance(current.GetType());
            foreach (var s in current)
            {
                int grade = (int)_fSorterGrade.GetValue(s);
                bool match = tier >= 4 ? grade >= 4 : grade == tier;
                if (match) filtered.Add(s);
            }

            if (filtered.Count == 0)
            {
                DSPCalculatorPlusLog.Warn("[tier] sorter Mk" + tier + " is not in the available sorters "
                    + "(locked under tech limit?) - leaving sorter tier on Auto.");
                return null;
            }

            DSPCalculatorPlusLog.Info("[tier] sorter forced to Mk" + tier + ".");
            return filtered;
        }
    }
}
