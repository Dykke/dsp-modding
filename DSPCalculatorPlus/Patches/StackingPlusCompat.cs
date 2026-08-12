using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace DSPCalculatorPlus
{
    /// <summary>
    /// Optional compatibility bridge to the StackingPlus mod
    /// (<c>com.zicarius.StackingPlus</c>), the third mod of the set.
    ///
    /// StackingPlus raises the real in-game cargo-stacking cap
    /// (<c>GameMain.history.inserterStackOutput</c>) above vanilla 4. DSPCalculator
    /// mirrors the stacking cap as <c>CalcDB.maxStackSize</c> (a static double, 4.0
    /// vanilla) and its overflow ceiling is <c>needBeltSpeed = demand / bpStack</c>
    /// vs the fastest belt - so a higher stack lifts the wall where an
    /// un-externalizable byproduct (hydrogen at scale) exceeds even 4x-stacked
    /// belts. DSPCalculatorPlus's overflow fix already forces
    /// <c>bpStackSetting = ReadMaxStack()</c> (i.e. CalcDB.maxStackSize, read live),
    /// and DSPCalculator's <c>bpStack</c> getter has NO clamp on a forced value.
    ///
    /// This shim simply keeps <c>CalcDB.maxStackSize</c> synced to the live cargo
    /// cap before each generation, so the overflow push can reach 8x (or whatever
    /// StackingPlus is configured to). No coupling to StackingPlus internals - it
    /// observes the game state StackingPlus produces. If StackingPlus isn't
    /// installed the live cap stays &lt;= 4 and this is a no-op (and it is gated on
    /// detection anyway, per the workspace's Start()-retry cross-mod rule).
    ///
    /// All reflection - no DSPCalculator source is copied. Gated by
    /// EnableStackingPlusCompat.
    /// </summary>
    internal static class StackingPlusCompat
    {
        internal const string StackingPlusGuid = "com.zicarius.StackingPlus";

        private static FieldInfo _fMaxStackSize; // CalcDB.maxStackSize (double)
        private static bool _ready;
        private static bool _present;
        private static bool _loggedPresent;

        public static void Apply(Harmony harmony)
        {
            var tCalcDB = AccessTools.TypeByName("DSPCalculator.Logic.CalcDB");
            _fMaxStackSize = tCalcDB != null ? AccessTools.Field(tCalcDB, "maxStackSize") : null;
            if (_fMaxStackSize == null)
            {
                DSPCalculatorPlusLog.Warn("[stackcompat] CalcDB.maxStackSize not found - StackingPlus compat disabled.");
                return;
            }

            var tConnector = AccessTools.TypeByName("DSPCalculator.Bp.BpConnector");
            var gen = tConnector != null ? AccessTools.Method(tConnector, "GenerateFullBlueprint") : null;
            if (gen == null)
            {
                DSPCalculatorPlusLog.Warn("[stackcompat] BpConnector.GenerateFullBlueprint not found - StackingPlus compat disabled.");
                return;
            }

            // High priority so maxStackSize is current before OverflowFixPatch's
            // postfix reads it (ReadMaxStack) during the overflow retry.
            harmony.Patch(gen, prefix: new HarmonyMethod(typeof(StackingPlusCompat), nameof(SyncPrefix)) { priority = Priority.High });
            _ready = true;
            DSPCalculatorPlusLog.Info("[stackcompat] installed: syncs CalcDB.maxStackSize to the live cargo-stacking cap before generation.");
        }

        /// <summary>
        /// Detect StackingPlus. Called from Awake (best-effort) AND Start():
        /// BepInEx loads plugins alphabetically, so StackingPlus ("S") loads after
        /// DSPCalculatorPlus ("D") and is not present at our Awake - the Start()
        /// retry is the reliable one (workspace cross-mod rule).
        /// </summary>
        public static void Detect()
        {
            try
            {
                bool present = Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(StackingPlusGuid);
                _present = present;
                if (present && !_loggedPresent)
                {
                    _loggedPresent = true;
                    DSPCalculatorPlusLog.Info("[stackcompat] StackingPlus detected - blueprints will plan against its raised stacking cap.");
                }
            }
            catch (Exception ex)
            {
                DSPCalculatorPlusLog.Warn("[stackcompat] detection failed: " + ex.Message);
            }
        }

        private static void SyncPrefix()
        {
            try
            {
                if (!_ready || !_present) return;
                if (!Plugin.Config.EnableStackingPlusCompat.Value) return;
                if (GameMain.history == null) return;

                int live = GameMain.history.inserterStackOutput;
                int target = live > 4 ? live : 4; // never drop DSPCalculator below its vanilla 4
                double cur = Convert.ToDouble(_fMaxStackSize.GetValue(null));
                if ((int)Math.Round(cur) != target)
                {
                    _fMaxStackSize.SetValue(null, (double)target);
                    DSPCalculatorPlusLog.Info("[stackcompat] CalcDB.maxStackSize " + cur + " -> " + target + " (live cargo-stacking cap).");
                }
            }
            catch (Exception ex)
            {
                DSPCalculatorPlusLog.Warn("[stackcompat] sync failed: " + ex.Message);
            }
        }
    }
}
