using System;
using HarmonyLib;

namespace EndlessResources
{
    /// <summary>
    /// Patch D - <c>StationComponent.DetermineDispatch</c>.
    ///
    /// This is the RISKIEST patch in the mod. <c>DetermineDispatch</c>
    /// is a long function (~400 lines) that walks the entire
    /// station network, deciding which source station dispatches
    /// which item to which destination. The source station's
    /// <c>storage[i].count</c> is decremented at multiple points
    /// inside the function (when the function commits to
    /// shipping an item).
    ///
    /// Strategy: snapshot the source station's storage in the
    /// prefix; restore it in the postfix. The Harmony priority
    /// is High so our postfix runs after any other mods' postfixes.
    /// The function's body may be refactored by DSP across
    /// versions, but the postfix is position-stable (it runs
    /// after the function returns), so the patch continues to
    /// work even if the body changes.
    ///
    /// Snapshot captures per storage slot: count, inc, localOrder,
    /// remoteOrder. The other StationStore fields (itemId, max,
    /// keepMode, etc.) are configuration that does not change
    /// during dispatch.
    /// </summary>
    [HarmonyPatch(typeof(StationComponent), nameof(StationComponent.DetermineDispatch))]
    internal static class StationDispatchPatch
    {
        internal sealed class Snapshot
        {
            public StationStore[] storage; // direct reference; the function does not realloc the array
        }

        private static bool firstFireLogged = true;

        [HarmonyPriority(Priority.High)]
        static void Prefix(
            StationComponent __instance,
            float shipSailSpeed,
            float shipWarpSpeed,
            int shipCarries,
            int priorityIndex,
            StationComponent[] gStationPool,
            FactoryProductionStat[] factoryStatPool,
            PlanetFactory[] factories,
            GalaxyData galaxy,
            TrafficStatistics tstat,
            ref Snapshot __state)
        {
            try
            {
                if (!Plugin.Config.EnableILSSourceFlag.Value) return;
                if (__instance == null || __instance.storage == null) return;

                __state = new Snapshot { storage = __instance.storage };
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch D (Station dispatch) prefix threw: " + ex);
            }
        }

        [HarmonyPriority(Priority.High)]
        static void Postfix(
            StationComponent __instance,
            float shipSailSpeed,
            float shipWarpSpeed,
            int shipCarries,
            int priorityIndex,
            StationComponent[] gStationPool,
            FactoryProductionStat[] factoryStatPool,
            PlanetFactory[] factories,
            GalaxyData galaxy,
            TrafficStatistics tstat,
            ref Snapshot __state)
        {
            try
            {
                if (__state == null || __state.storage == null) return;
                if (!Plugin.Config.EnableILSSourceFlag.Value) return;
                if (__instance == null) return;

                var storage = __state.storage;
                if (__instance.storage == null || __instance.storage.Length != storage.Length) return;

                bool anyChanged = false;
                for (int i = 0; i < storage.Length; i++)
                {
                    // The storage array reference is stable (the
                    // function never reallocs it). So we just need
                    // to restore the per-slot fields.
                    if (__instance.storage[i].count != storage[i].count ||
                        __instance.storage[i].inc != storage[i].inc ||
                        __instance.storage[i].localOrder != storage[i].localOrder ||
                        __instance.storage[i].remoteOrder != storage[i].remoteOrder)
                    {
                        __instance.storage[i].count = storage[i].count;
                        __instance.storage[i].inc = storage[i].inc;
                        __instance.storage[i].localOrder = storage[i].localOrder;
                        __instance.storage[i].remoteOrder = storage[i].remoteOrder;
                        anyChanged = true;
                    }
                }

                if (anyChanged && firstFireLogged && EndlessResourcesLog.IsDebugEnabled())
                {
                    EndlessResourcesLog.Info("[patch] Patch D (Station dispatch) fired: first storage restoration. slots=" + storage.Length);
                    firstFireLogged = false;
                }
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch D (Station dispatch) postfix threw: " + ex);
            }
        }
    }
}
