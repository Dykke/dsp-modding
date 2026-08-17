using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DSPCalculatorPlus
{
    /// <summary>
    /// Group B - overflow fix (Phase 3), "external logistics input" strategy.
    ///
    /// DSPCalculator aborts blueprint generation when an item's total flow
    /// exceeds one belt of the fastest tier. Instead of failing, this supplies
    /// over-capacity items as EXTERNAL logistics inputs (DSPCalculator's
    /// blackbox model): the item is reclassified `consideredAsOre`, the
    /// solution is re-planned so it becomes a leaf input, and DSPCalculator's
    /// own station/port machinery feeds it.
    ///
    /// Not every over-capacity item CAN be externalized:
    ///   - byproducts (e.g. hydrogen) are produced regardless of being marked
    ///     "ore", so they keep overflowing;
    ///   - target outputs can't become inputs.
    /// These "stuck" items are detected (they reappear after being tried) and
    /// the generation is finalized by letting it proceed anyway with those
    /// items under-provisioned on a single belt, rather than failing outright.
    /// The log names exactly which items externalized vs. stuck.
    ///
    /// All reflection - no DSPCalculator source is copied. Gated by
    /// EnableMultiLaneOverflowFix.
    /// </summary>
    internal static class OverflowFixPatch
    {
        private static FieldInfo _fSolution;          // BpConnector.solution
        private static FieldInfo _fItemSumInfos;      // BpConnector.itemSumInfos
        private static FieldInfo _fBeltsAvailable;    // SolutionTree.beltsAvailable
        private static FieldInfo _fUserPreference;    // SolutionTree.userPreference
        private static FieldInfo _fTargets;           // SolutionTree.targets
        private static FieldInfo _fItemConfigs;       // UserPreference.itemConfigs
        private static FieldInfo _fConsideredAsOre;   // ItemConfig.consideredAsOre
        private static ConstructorInfo _ctorItemConfig; // ItemConfig(int)
        private static FieldInfo _fSumNeedSpeed;      // BpItemSumInfo.needBeltSpeed
        private static FieldInfo _fSumItemId;         // BpItemSumInfo.itemId
        private static FieldInfo _fSumNeedBeltId;     // BpItemSumInfo.needBeltId
        private static FieldInfo _fBeltSpeed;         // BpBeltInfo.speedPerMin
        private static FieldInfo _fBeltItemId;        // BpBeltInfo.itemId
        private static FieldInfo _fTargetItemId;      // ItemTarget.itemId
        private static FieldInfo _fTargetSpeed;       // ItemTarget.speed
        private static FieldInfo _fForceNotOre;       // ItemConfig.forceNotOre
        private static FieldInfo _fBpStackSetting;    // UserPreference.bpStackSetting (0=auto,1..4 forced)
        private static FieldInfo _fMaxStackSize;      // CalcDB.maxStackSize (double) - DSPCalculator's stacking cap (4 vanilla; a future mod may raise it)
        private static MethodInfo _mReSolve;          // SolutionTree.ReSolve(double)  -- does ClearTree()+Solve()
        private static MethodInfo _mGenerateFull;     // BpConnector.GenerateFullBlueprint
        private static bool _ready;

        // Per-generation state.
        private static readonly HashSet<int> _pendingExternal = new HashSet<int>(); // overflow items from the latest CalcItemSumInfos
        private static readonly Dictionary<int, bool> _restoreOreValue = new Dictionary<int, bool>();
        private static readonly Dictionary<int, bool> _restoreForceNotOre = new Dictionary<int, bool>();
        private static readonly HashSet<int> _createdConfigs = new HashSet<int>();
        private static bool _inRetry;
        private static bool _finalizeUnderProvision;

        /// <summary>itemId -&gt; belts needed, from the most recent generation (diagnostic).</summary>
        internal static Dictionary<int, int> LastRunLaneCounts = new Dictionary<int, int>();

        public static void Apply(Harmony harmony)
        {
            var tConnector = AccessTools.TypeByName("DSPCalculator.Bp.BpConnector");
            if (tConnector == null) { DSPCalculatorPlusLog.Warn("[overflow] BpConnector not found - overflow fix disabled."); return; }

            var calcTarget = AccessTools.Method(tConnector, "CalcItemSumInfos");
            _mGenerateFull = AccessTools.Method(tConnector, "GenerateFullBlueprint");
            if (calcTarget == null || _mGenerateFull == null)
            {
                DSPCalculatorPlusLog.Warn("[overflow] CalcItemSumInfos/GenerateFullBlueprint not found - overflow fix disabled.");
                return;
            }

            var tSumInfo = AccessTools.TypeByName("DSPCalculator.Bp.BpItemSumInfo");
            var tBeltInfo = AccessTools.TypeByName("DSPCalculator.Bp.BpBeltInfo");
            var tItemConfig = AccessTools.TypeByName("DSPCalculator.Logic.ItemConfig");
            var tItemTarget = AccessTools.TypeByName("DSPCalculator.Logic.ItemTarget");

            _fSolution = AccessTools.Field(tConnector, "solution");
            var tSolution = _fSolution?.FieldType;
            _fItemSumInfos = AccessTools.Field(tConnector, "itemSumInfos");
            _fBeltsAvailable = tSolution != null ? AccessTools.Field(tSolution, "beltsAvailable") : null;
            _fUserPreference = tSolution != null ? AccessTools.Field(tSolution, "userPreference") : null;
            _fTargets = tSolution != null ? AccessTools.Field(tSolution, "targets") : null;
            _mReSolve = tSolution != null ? AccessTools.Method(tSolution, "ReSolve", new[] { typeof(double) }) : null;
            var tUserPref = _fUserPreference?.FieldType;
            _fItemConfigs = tUserPref != null ? AccessTools.Field(tUserPref, "itemConfigs") : null;
            _fConsideredAsOre = tItemConfig != null ? AccessTools.Field(tItemConfig, "consideredAsOre") : null;
            _fForceNotOre = tItemConfig != null ? AccessTools.Field(tItemConfig, "forceNotOre") : null;
            _fBpStackSetting = tUserPref != null ? AccessTools.Field(tUserPref, "bpStackSetting") : null;
            // Dynamic stacking cap - read live so a future belt-stacking mod that
            // raises CalcDB.maxStackSize is picked up automatically (no hardcoded 4).
            var tCalcDB = AccessTools.TypeByName("DSPCalculator.Logic.CalcDB");
            _fMaxStackSize = tCalcDB != null ? AccessTools.Field(tCalcDB, "maxStackSize") : null;
            _ctorItemConfig = tItemConfig != null ? AccessTools.Constructor(tItemConfig, new[] { typeof(int) }) : null;
            _fSumNeedSpeed = tSumInfo != null ? AccessTools.Field(tSumInfo, "needBeltSpeed") : null;
            _fSumItemId = tSumInfo != null ? AccessTools.Field(tSumInfo, "itemId") : null;
            _fSumNeedBeltId = tSumInfo != null ? AccessTools.Field(tSumInfo, "needBeltId") : null;
            _fBeltSpeed = tBeltInfo != null ? AccessTools.Field(tBeltInfo, "speedPerMin") : null;
            _fBeltItemId = tBeltInfo != null ? AccessTools.Field(tBeltInfo, "itemId") : null;
            _fTargetItemId = tItemTarget != null ? AccessTools.Field(tItemTarget, "itemId") : null;
            _fTargetSpeed = tItemTarget != null ? AccessTools.Field(tItemTarget, "speed") : null;

            if (_fSolution == null || _fItemSumInfos == null || _fBeltsAvailable == null
                || _fUserPreference == null || _fTargets == null || _mReSolve == null
                || _fItemConfigs == null || _fConsideredAsOre == null || _fForceNotOre == null || _ctorItemConfig == null
                || _fSumNeedSpeed == null || _fSumItemId == null || _fSumNeedBeltId == null
                || _fBeltSpeed == null || _fBeltItemId == null || _fTargetItemId == null || _fTargetSpeed == null)
            {
                DSPCalculatorPlusLog.Error("[overflow] one or more DSPCalculator members not found - overflow fix disabled.");
                return;
            }

            _ready = true;
            harmony.Patch(calcTarget, postfix: new HarmonyMethod(typeof(OverflowFixPatch), nameof(CalcPostfix)) { priority = Priority.Low });
            harmony.Patch(_mGenerateFull,
                prefix: new HarmonyMethod(typeof(OverflowFixPatch), nameof(GenPrefix)),
                postfix: new HarmonyMethod(typeof(OverflowFixPatch), nameof(GenPostfix)) { priority = Priority.Low });

            // DIAGNOSTIC (retry-only): log each downstream generation stage's
            // return value during our internal regenerate, to pinpoint WHICH
            // stage fails at scale when Phase B force-past-overflow still yields
            // a failed blueprint (belt-height shows a popup; a silent stage does
            // not - this names it). FALSE lines are Warn (always shown).
            foreach (var stage in new[] { "GenProcessors", "ArrangeBpBlocks", "PlaceBuildings", "ConnectBlocks" })
            {
                var sm = AccessTools.Method(tConnector, stage);
                if (sm != null && sm.ReturnType == typeof(bool))
                    harmony.Patch(sm, postfix: new HarmonyMethod(typeof(OverflowFixPatch), nameof(StagePostfix)));
            }

            // DIAGNOSTIC + spam-suppression: intercept the game's UIMessageBox.Show
            // so that during our internal regeneration we LOG the exact failure
            // dialog (title/content) instead of popping dozens of dialogs. This
            // reveals *why* generation fails downstream of the overflow check.
            var tMsgBox = AccessTools.TypeByName("UIMessageBox");
            if (tMsgBox != null)
            {
                int patched = 0;
                foreach (var m in tMsgBox.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "Show") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string))
                    {
                        try { harmony.Patch(m, prefix: new HarmonyMethod(typeof(OverflowFixPatch), nameof(MsgBoxPrefix))); patched++; }
                        catch (Exception ex) { DSPCalculatorPlusLog.Warn("[overflow] could not patch a UIMessageBox.Show overload: " + ex.Message); }
                    }
                }
                DSPCalculatorPlusLog.Info("[overflow] diagnostic: patched " + patched + " UIMessageBox.Show overload(s).");
            }
            else
            {
                DSPCalculatorPlusLog.Warn("[overflow] UIMessageBox type not found - failure-dialog diagnostic unavailable.");
            }

            DSPCalculatorPlusLog.Info("[overflow] Group B (external-input) patch installed on CalcItemSumInfos + GenerateFullBlueprint.");
        }

        // While we are regenerating internally (_inRetry), log the dialog's
        // title/content (the failure reason) and skip showing it. Outside our
        // regeneration, behave normally (return true).
        private static bool MsgBoxPrefix(string __0, string __1, ref object __result)
        {
            if (!_inRetry) return true;
            // Info (DebugLog-gated), not Warn: these are DSPCalculator's own
            // dialogs during OUR internal retry, suppressed from the game UI -
            // expected noise, not a real failure (generation succeeds after).
            DSPCalculatorPlusLog.Info("[overflow][diag] DSPCalculator dialog during regenerate (suppressed): \"" + __0 + "\" | \"" + __1 + "\"");
            __result = null;
            return false;
        }

        /// <summary>DSPCalculator's live stacking cap (CalcDB.maxStackSize; 4
        /// vanilla). Read dynamically so a future belt-stacking mod that raises
        /// it is honoured automatically. Falls back to 4 if unreadable.</summary>
        private static int ReadMaxStack()
        {
            if (_fMaxStackSize != null)
            {
                try
                {
                    int v = (int)Math.Round(Convert.ToDouble(_fMaxStackSize.GetValue(null)));
                    if (v >= 1) return v;
                }
                catch { /* fall through to default */ }
            }
            return 4;
        }

        // DIAGNOSTIC: names the downstream stage that fails during a retry.
        private static void StagePostfix(bool __result, MethodBase __originalMethod)
        {
            if (!_inRetry) return;
            if (!__result)
                DSPCalculatorPlusLog.Warn("[overflow][stage] " + __originalMethod.Name + " returned FALSE <- failing stage.");
            else
                DSPCalculatorPlusLog.Info("[overflow][stage] " + __originalMethod.Name + " ok.");
        }

        private static void GenPrefix()
        {
            if (_inRetry) return;
            _pendingExternal.Clear();
            LastRunLaneCounts.Clear();
        }

        // Detect over-capacity items into _pendingExternal (fresh each call).
        // In finalize mode, also assign belts + flip the result so a blueprint
        // is produced despite any remaining (stuck) overflow.
        private static void CalcPostfix(object __instance, ref bool __result)
        {
            if (!_ready || __instance == null) return;
            if (!Plugin.Config.EnableMultiLaneOverflowFix.Value) return;

            object solution = _fSolution.GetValue(__instance);
            if (solution == null) return;
            var belts = _fBeltsAvailable.GetValue(solution) as IList;
            if (belts == null || belts.Count == 0) return;
            var sumInfos = _fItemSumInfos.GetValue(__instance) as IDictionary;
            if (sumInfos == null) return;

            object fastest = belts[belts.Count - 1];
            double fastestSpeed = (double)_fBeltSpeed.GetValue(fastest);
            int fastestId = (int)_fBeltItemId.GetValue(fastest);
            if (fastestSpeed <= 0.0) return;

            var targetIds = GetTargetItemIds(solution);

            _pendingExternal.Clear();
            foreach (object si in sumInfos.Values)
            {
                double need = (double)_fSumNeedSpeed.GetValue(si);
                int itemId = (int)_fSumItemId.GetValue(si);

                if (_finalizeUnderProvision)
                {
                    // Ensure every item has a belt so downstream doesn't get -1.
                    if ((int)_fSumNeedBeltId.GetValue(si) <= 0)
                        _fSumNeedBeltId.SetValue(si, PickBelt(belts, need, fastestId));
                    continue;
                }

                if (need <= fastestSpeed) continue;
                LastRunLaneCounts[itemId] = (int)Math.Ceiling(need / fastestSpeed - 1e-6);
                if (targetIds.Contains(itemId)) continue; // targets can't be externalized (handled as stuck at finalize)
                _pendingExternal.Add(itemId);
            }

            if (_finalizeUnderProvision) __result = true; // let generation proceed
        }

        private static void GenPostfix(object __instance, ref bool __result, int genLevel, bool forcePortOnLeft, bool orthogonalConnect)
        {
            if (!_ready || __instance == null) return;
            if (!Plugin.Config.EnableMultiLaneOverflowFix.Value) return;
            if (_inRetry) return;
            if (__result) return;                    // generation already succeeded (no overflow)
            if (_pendingExternal.Count == 0) return; // failed for a non-overflow reason - don't touch

            object solution = _fSolution.GetValue(__instance);
            object userPref = solution != null ? _fUserPreference.GetValue(solution) : null;
            var itemConfigs = userPref != null ? _fItemConfigs.GetValue(userPref) as IDictionary : null;
            if (itemConfigs == null) return;

            // Snapshot the primary target's speed - ReSolve()/root-shrinking can
            // mutate it, so we anchor every re-solve to this and restore it.
            double origSpeed = GetPrimaryTargetSpeed(solution);

            // Optional last-resort lever (PushBeltStackingOnOverflow): raising
            // belt-stacking to the vanilla max (4x) multiplies per-belt capacity,
            // which is exactly what a failing block-level belt check
            // (bpCountToSatisfy>1, e.g. an un-externalizable hydrogen byproduct)
            // needs. Declared out here so finally can restore it.
            bool stackRaised = false;
            int origStack = 0;

            _inRetry = true;
            _restoreOreValue.Clear();
            _restoreForceNotOre.Clear();
            _createdConfigs.Clear();
            var externalized = new List<int>();
            var alreadyTried = new HashSet<int>();
            try
            {
                bool result = false;
                int pass = 0;

                // Try raising belt-stacking to the max FIRST - it often clears
                // the overflow with no externalization at all (cleanest result).
                // The cap is read live (CalcDB.maxStackSize), so a future
                // stacking mod that raises it is used automatically.
                int maxStack = ReadMaxStack();
                if (Plugin.Config.PushBeltStackingOnOverflow.Value && _fBpStackSetting != null && userPref != null && maxStack > 1)
                {
                    origStack = (int)_fBpStackSetting.GetValue(userPref);
                    if (origStack != maxStack)
                    {
                        _fBpStackSetting.SetValue(userPref, maxStack);
                        stackRaised = true;
                        DSPCalculatorPlusLog.Info("[overflow] raising belt-stacking to " + maxStack + "x (was " + origStack + ") to lift the belt-capacity ceiling.");
                        result = (bool)_mGenerateFull.Invoke(__instance, new object[] { genLevel, forcePortOnLeft, orthogonalConnect });
                        if (result) DSPCalculatorPlusLog.Info("[overflow] " + maxStack + "x belt-stacking alone resolved it - no externalization needed.");
                    }
                }

                // Phase A: externalize newly-seen overflow items until only
                // stuck ones remain (nothing new to try).
                while (!result && pass < 40)
                {
                    var newItems = new List<int>();
                    foreach (int id in _pendingExternal)
                        if (!alreadyTried.Contains(id)) newItems.Add(id);
                    if (newItems.Count == 0) break; // converged - only stuck items (or none) remain
                    pass++;
                    foreach (int id in newItems)
                    {
                        MarkConsideredAsOre(itemConfigs, id);
                        alreadyTried.Add(id);
                        externalized.Add(id);
                    }
                    // ReSolve() = ClearTree()+Solve(); the ClearTree() is what
                    // rebuilds recipeInfos so the ore reclassification takes effect.
                    // (Bare Solve() reused the stale plan and pruned nothing.)
                    _mReSolve.Invoke(solution, new object[] { origSpeed });
                    result = (bool)_mGenerateFull.Invoke(__instance, new object[] { genLevel, forcePortOnLeft, orthogonalConnect });
                    if (result) break; // fully resolved by externalizing
                }

                // Phase B: if still failing, some items are stuck (byproducts /
                // targets). Finalize by generating anyway (stuck items on a
                // single belt).
                var stuck = new List<int>(_pendingExternal);
                if (!result)
                {
                    // DIAGNOSTIC: any UIMessageBox that fires between these two
                    // lines names the DOWNSTREAM failure stage (belt-height game
                    // limit / "Unexpected Error 503" from ConnectBlocks / etc.).
                    // If none fires and Phase B still returns false, the failure
                    // is a SILENT stage (GenProcessors/ArrangeBpBlocks/PlaceBuildings).
                    DSPCalculatorPlusLog.Info("[overflow] Phase B: finalizing (force past single-belt check) with "
                        + stuck.Count + " stuck item(s) under-provisioned [" + Join(stuck) + "]...");
                    _finalizeUnderProvision = true;
                    result = (bool)_mGenerateFull.Invoke(__instance, new object[] { genLevel, forcePortOnLeft, orthogonalConnect });
                    _finalizeUnderProvision = false;
                    DSPCalculatorPlusLog.Info("[overflow] Phase B: generation returned " + result
                        + (result ? "" : " (see any [overflow][diag] dialog line just above for the downstream reason; "
                            + "if there is none, a silent placement/connection stage failed at this scale).") + ".");
                }

                __result = result;
                DSPCalculatorPlusLog.Info("[overflow] marked " + externalized.Count + " item(s) as external over " + pass
                    + " pass(es) [" + Join(externalized) + "]; of those, " + stuck.Count + " did NOT prune (stuck, "
                    + "under-provisioned) [" + Join(stuck) + "] => " + (externalized.Count - stuck.Count)
                    + " genuinely externalized; generation " + (result ? "succeeded" : "FAILED")
                    + (stackRaised && result ? " (using " + maxStack + "x belt-stacking - blueprint ASSUMES that cargo stacking; needs pile/proliferator tech to run at full rate)" : "")
                    + ".");
                if (stuck.Count > 0)
                    DSPCalculatorPlusLog.Warn("[overflow] stuck items (byproducts or targets that can't become inputs) are on a single "
                        + "belt and under-provisioned: [" + Join(stuck) + "]. Add extra output/input belts for these manually.");
                if (!result)
                    DSPCalculatorPlusLog.Warn("[overflow] could not generate even after externalizing"
                        + (stackRaised ? " + " + maxStack + "x belt-stacking" : "")
                        + " - a byproduct's belt demand still exceeds capacity at this scale. "
                        + "Reduce the target quantity, or split into multiple smaller blueprints"
                        + (stackRaised ? " (already at max " + maxStack + "x stacking)." : (Plugin.Config.PushBeltStackingOnOverflow.Value ? "." : ", or enable PushBeltStackingOnOverflow to try higher stacking.")));
            }
            catch (Exception ex)
            {
                DSPCalculatorPlusLog.Error("[overflow] externalize-and-regenerate failed: " + ex);
            }
            finally
            {
                _finalizeUnderProvision = false;
                if (stackRaised)
                {
                    try { _fBpStackSetting.SetValue(userPref, origStack); }
                    catch (Exception ex) { DSPCalculatorPlusLog.Error("[overflow] restore bpStackSetting failed: " + ex.Message); }
                }
                RestoreItemConfigs(itemConfigs);
                // Restore the user's solution exactly: original item classes +
                // original target speed, then ReSolve so the calc window matches.
                try
                {
                    SetPrimaryTargetSpeed(solution, origSpeed);
                    _mReSolve.Invoke(solution, new object[] { origSpeed });
                }
                catch (Exception ex) { DSPCalculatorPlusLog.Error("[overflow] restore re-solve failed: " + ex.Message); }
                _pendingExternal.Clear();
                _inRetry = false;
            }
        }

        private static double GetPrimaryTargetSpeed(object solution)
        {
            var targets = _fTargets.GetValue(solution) as IList;
            if (targets == null || targets.Count == 0) return 0.0;
            return (double)_fTargetSpeed.GetValue(targets[0]);
        }

        private static void SetPrimaryTargetSpeed(object solution, double speed)
        {
            var targets = _fTargets.GetValue(solution) as IList;
            if (targets == null || targets.Count == 0) return;
            _fTargetSpeed.SetValue(targets[0], speed);
        }

        private static int PickBelt(IList belts, double need, int fastestId)
        {
            for (int i = 0; i < belts.Count; i++)
                if ((double)_fBeltSpeed.GetValue(belts[i]) >= need)
                    return (int)_fBeltItemId.GetValue(belts[i]);
            return fastestId;
        }

        private static HashSet<int> GetTargetItemIds(object solution)
        {
            var set = new HashSet<int>();
            var targets = _fTargets.GetValue(solution) as IList;
            if (targets == null) return set;
            foreach (object t in targets)
                if (t != null) set.Add((int)_fTargetItemId.GetValue(t));
            return set;
        }

        private static void MarkConsideredAsOre(IDictionary itemConfigs, int itemId)
        {
            object cfg;
            if (itemConfigs.Contains(itemId))
            {
                cfg = itemConfigs[itemId];
                if (!_restoreOreValue.ContainsKey(itemId))
                {
                    _restoreOreValue[itemId] = (bool)_fConsideredAsOre.GetValue(cfg);
                    _restoreForceNotOre[itemId] = (bool)_fForceNotOre.GetValue(cfg);
                }
            }
            else
            {
                cfg = _ctorItemConfig.Invoke(new object[] { itemId });
                itemConfigs[itemId] = cfg;
                _createdConfigs.Add(itemId);
            }
            // Match DSPCalculator's own "consider as raw ore" toggle exactly.
            _fConsideredAsOre.SetValue(cfg, true);
            _fForceNotOre.SetValue(cfg, false);
        }

        private static void RestoreItemConfigs(IDictionary itemConfigs)
        {
            foreach (int itemId in _createdConfigs)
                if (itemConfigs.Contains(itemId)) itemConfigs.Remove(itemId);
            foreach (var kv in _restoreOreValue)
                if (itemConfigs.Contains(kv.Key))
                {
                    object cfg = itemConfigs[kv.Key];
                    _fConsideredAsOre.SetValue(cfg, kv.Value);
                    if (_restoreForceNotOre.ContainsKey(kv.Key))
                        _fForceNotOre.SetValue(cfg, _restoreForceNotOre[kv.Key]);
                }
        }

        private static string Join(ICollection<int> items)
        {
            var arr = new string[items.Count];
            int i = 0;
            foreach (int v in items) arr[i++] = v.ToString();
            return string.Join(",", arr);
        }
    }
}
