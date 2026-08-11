using System;
using HarmonyLib;

namespace EndlessResources
{
    /// <summary>
    /// Patch C - <c>StationComponent.UpdateVeinCollection</c>.
    ///
    /// The ILS / PLS vein collector pulls items from its
    /// internal <c>MinerComponent</c>'s <c>productCount</c> and
    /// into the station's <c>storage[0].count</c>. The vein
    /// amount itself is decremented by <c>MinerComponent.InternalUpdate</c>
    /// (Patch A), not by this function. So this patch is
    /// a SPEED BOOST, not a redundancy:
    ///
    /// Without Patch C, the miner's productCount is consumed
    /// by UpdateVeinCollection; the next tick the miner
    /// must run an InternalUpdate to fill it back up. If the
    /// station is a fast collector, the miner's productCount
    /// can become the bottleneck.
    ///
    /// With Patch C, the miner's productCount is restored to
    /// its pre-call value each tick, so the next tick the
    /// collector can pull from a full buffer. Combined with
    /// Patch A (vein amount restored), the ILS / PLS vein
    /// collector ships at maximum rate indefinitely.
    /// </summary>
    [HarmonyPatch(typeof(StationComponent), nameof(StationComponent.UpdateVeinCollection))]
    internal static class StationVeinCollectionPatch
    {
        internal sealed class Snapshot
        {
            public int minerId;
            public int productCount;
        }

        private static bool firstFireLogged = true;

        static void Prefix(
            StationComponent __instance,
            PlanetFactory factory,
            int[] productRegister,
            ref Snapshot __state)
        {
            try
            {
                if (!Plugin.Config.EnableILSVeinCollectionFlag.Value) return;
                if (__instance == null) return;
                if (factory == null || factory.factorySystem == null) return;
                var minerPool = factory.factorySystem.minerPool;
                if (minerPool == null) return;

                int mid = __instance.minerId;
                if (mid <= 0 || mid >= minerPool.Length) return;
                // minerPool[mid] is a struct COPY of MinerComponent (a value
                // type). We can read fields from it freely, but writes go to
                // the local copy. Only the prefix needs a copy (for the
                // snapshot); the postfix writes to the array slot directly.
                var miner = minerPool[mid];

                __state = new Snapshot
                {
                    minerId = mid,
                    productCount = miner.productCount,
                };
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch C (Station vein collection) prefix threw: " + ex);
            }
        }

        [HarmonyPriority(Priority.High)]
        static void Postfix(
            StationComponent __instance,
            PlanetFactory factory,
            int[] productRegister,
            ref Snapshot __state)
        {
            try
            {
                if (__state == null) return;
                if (!Plugin.Config.EnableILSVeinCollectionFlag.Value) return;
                if (factory == null || factory.factorySystem == null) return;
                var minerPool = factory.factorySystem.minerPool;
                if (minerPool == null) return;
                if (__state.minerId <= 0 || __state.minerId >= minerPool.Length) return;

                // Write directly to the array slot, not to a local copy.
                // MinerComponent is a struct; modifying the local copy would
                // be silently discarded.
                if (minerPool[__state.minerId].productCount != __state.productCount)
                {
                    minerPool[__state.minerId].productCount = __state.productCount;

                    if (firstFireLogged && EndlessResourcesLog.IsDebugEnabled())
                    {
                        EndlessResourcesLog.Info("[patch] Patch C (Station vein collection) fired: miner " + __state.minerId + " productCount restored to " + __state.productCount);
                        firstFireLogged = false;
                    }
                }
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch C (Station vein collection) postfix threw: " + ex);
            }
        }
    }
}
