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
            DSPCalculatorPlusLog.Warn("[overflow][diag] DSPCalculator dialog during regenerate (suppressed): \"" + __0 + "\" | \"" + __1 + "\"");
            __result = null;
            return false;
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
                // Phase A: externalize newly-seen overflow items until only
                // stuck ones remain (nothing new to try).
                while (pass < 40)
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
                    _finalizeUnderProvision = true;
                    result = (bool)_mGenerateFull.Invoke(__instance, new object[] { genLevel, forcePortOnLeft, orthogonalConnect });
                    _finalizeUnderProvision = false;
                }

                __result = result;
                DSPCalculatorPlusLog.Info("[overflow] marked " + externalized.Count + " item(s) as external over " + pass
                    + " pass(es) [" + Join(externalized) + "]; of those, " + stuck.Count + " did NOT prune (stuck, "
                    + "under-provisioned) [" + Join(stuck) + "] => " + (externalized.Count - stuck.Count)
                    + " genuinely externalized; generation " + (result ? "succeeded" : "FAILED") + ".");
                if (stuck.Count > 0)
                    DSPCalculatorPlusLog.Warn("[overflow] stuck items (byproducts or targets that can't become inputs) are on a single "
                        + "belt and under-provisioned: [" + Join(stuck) + "]. Add extra output/input belts for these manually.");
            }
            catch (Exception ex)
            {
                DSPCalculatorPlusLog.Error("[overflow] externalize-and-regenerate failed: " + ex);
            }
            finally
            {
                _finalizeUnderProvision = false;
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
