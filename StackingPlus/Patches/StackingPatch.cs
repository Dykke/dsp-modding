using HarmonyLib;
using System;

namespace StackingPlus
{
    /// <summary>
    /// Core stacking enforcement. DSP's cargo stacking caps are plain public int
    /// fields on <c>GameHistoryData</c>, set by <c>UnlockTechFunction</c> and read
    /// live at build/refresh time (verified against the decompile - see
    /// StackingPlus/notes/stackingplus.md). There is NO hardcoded "4" clamp in the
    /// insert path; <c>Cargo.stack</c> is a byte, so 255 is the true ceiling.
    ///
    /// Strategy: after any history change (tech unlock, save load, new game), if a
    /// dimension has reached its vanilla ceiling (or tech-gating is off), raise the
    /// field to the configured cap and refresh existing sorters via the game's own
    /// <c>GameData.OnInserterTechChange()</c>. All field access is defensive: a DSP
    /// rename disables that one dimension with a warning instead of crashing.
    /// </summary>
    internal static class StackingPatch
    {
        private static AccessTools.FieldRef<GameHistoryData, int> _outputRef;
        private static AccessTools.FieldRef<GameHistoryData, int> _inputRef;
        private static AccessTools.FieldRef<GameHistoryData, int> _pilerRef;
        private static bool _outputOk, _inputOk, _pilerOk;

        /// <summary>Resolve the reflected field accessors once. Call from Awake.</summary>
        internal static void Init()
        {
            _outputRef = TryFieldRef("inserterStackOutput", out _outputOk);
            _inputRef = TryFieldRef("inserterStackInput", out _inputOk);
            _pilerRef = TryFieldRef("stationPilerLevel", out _pilerOk);
        }

        private static AccessTools.FieldRef<GameHistoryData, int> TryFieldRef(string name, out bool ok)
        {
            try
            {
                var r = AccessTools.FieldRefAccess<GameHistoryData, int>(name);
                ok = r != null;
                if (!ok) StackingPlusLog.Warn("[compat] GameHistoryData." + name + " not found; that dimension is disabled.");
                return r;
            }
            catch (Exception ex)
            {
                ok = false;
                StackingPlusLog.Warn("[compat] GameHistoryData." + name + " not found; that dimension is disabled: " + ex.Message);
                return null;
            }
        }

        // Set when an inserter field changed while the game wasn't ready to
        // refresh (e.g. during save load) - retried from OnGameBegin.
        private static bool _pendingRefresh;

        // -- Harmony postfixes (bound manually in Plugin.Awake) -----------------
        // Import runs mid-load (factory data not ready) -> defer the sorter
        // refresh. UnlockTechFunction / SetForNewGame run mid-game -> safe to
        // refresh immediately.
        internal static void AfterUnlockTech(GameHistoryData __instance) { Enforce(__instance, true); }
        internal static void AfterImport(GameHistoryData __instance) { Enforce(__instance, false); }
        internal static void AfterSetForNewGame(GameHistoryData __instance) { Enforce(__instance, true); }

        /// <summary>Postfix on GameMain.Begin - runs after data is loaded.</summary>
        internal static void OnGameBegin()
        {
            BeltSpeedPatch.EnsureProtoScaled();
            if (_pendingRefresh) SafeRefresh(GameMain.history);
        }

        /// <summary>Best-effort enforcement against the current history, if any.</summary>
        internal static void EnforceCurrent()
        {
            try
            {
                if (GameMain.instance != null && GameMain.history != null) Enforce(GameMain.history, true);
            }
            catch (Exception ex)
            {
                StackingPlusLog.Warn("EnforceCurrent failed: " + ex.Message);
            }
        }

        internal static void Enforce(GameHistoryData history, bool refreshInserters)
        {
            if (history == null) return;
            try
            {
                bool inserterChanged = false;

                if (ModSettings.EnableSorterOutput && _outputOk)
                    inserterChanged |= Apply(_outputRef, history, "SorterOutput",
                        ModSettings.SorterOutputCap, ModSettings.VanillaCeilingOutput);

                if (ModSettings.EnableSorterInput && _inputOk)
                    inserterChanged |= Apply(_inputRef, history, "SorterInput",
                        ModSettings.SorterInputCap, ModSettings.VanillaCeilingInput);

                if (ModSettings.EnableStationPiler && _pilerOk)
                    Apply(_pilerRef, history, "StationPiler",
                        ModSettings.StationPilerCap, ModSettings.VanillaCeilingPiler);

                if (ModSettings.EnableDeliveryPackage)
                    ApplyDelivery();

                if (inserterChanged)
                {
                    if (refreshInserters) SafeRefresh(history);
                    else _pendingRefresh = true;
                }
            }
            catch (Exception ex)
            {
                StackingPlusLog.Error("Enforce failed: " + ex);
            }
        }

        /// <summary>Refresh existing sorters; retried later if the game isn't ready.</summary>
        private static void SafeRefresh(GameHistoryData history)
        {
            try
            {
                if (history != null && history.gameData != null)
                {
                    history.gameData.OnInserterTechChange();
                    _pendingRefresh = false;
                    StackingPlusLog.Info("[stack] refreshed existing sorters.");
                }
            }
            catch (Exception ex)
            {
                _pendingRefresh = true; // retry from OnGameBegin
                StackingPlusLog.Info("[stack] sorter refresh deferred (game not ready): " + ex.Message);
            }
        }

        /// <summary>Raise-only: never lowers a value the player already has.</summary>
        private static bool Apply(AccessTools.FieldRef<GameHistoryData, int> fieldRef,
            GameHistoryData history, string label, int cap, int ceiling)
        {
            int target = ModSettings.ClampStack(cap);
            int cur = fieldRef(history);

            if (ModSettings.TechGated && cur < ceiling) return false; // wait for vanilla max
            if (target <= cur) return false;                          // raise-only

            fieldRef(history) = target;
            StackingPlusLog.Info("[stack] " + label + ": " + cur + " -> " + target
                + " (gated=" + ModSettings.TechGated + ", ceiling=" + ceiling + ")");
            return true;
        }

        private static void ApplyDelivery()
        {
            try
            {
                var player = GameMain.mainPlayer;
                if (player == null) return;
                var dp = player.deliveryPackage;
                if (dp == null) return;

                int target = ModSettings.ClampStack(ModSettings.DeliveryPackageCap);
                int cur = dp.stackSizeMultiplier;

                if (ModSettings.TechGated && cur < ModSettings.VanillaCeilingPackage) return; // wait for vanilla max
                if (target <= cur) return;                                                    // raise-only

                dp.stackSizeMultiplier = target;
                StackingPlusLog.Info("[stack] DeliveryPackage: " + cur + " -> " + target
                    + " (gated=" + ModSettings.TechGated + ", ceiling=" + ModSettings.VanillaCeilingPackage + ")");
            }
            catch (Exception ex)
            {
                StackingPlusLog.Warn("ApplyDelivery failed: " + ex.Message);
            }
        }
    }
}
