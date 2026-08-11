using System;
using HarmonyLib;

namespace EndlessResources
{
    /// <summary>
    /// Patch B - <c>PlayerAction_Mine.GameTick</c>.
    ///
    /// Restores the vein's amount after the Icarus mecha
    /// hand-mines. The decrement happens at one specific
    /// line inside GameTick (the line guarded by
    /// <c>!isInfiniteResource</c>). The postfix runs after the
    /// decrement, restores the snapshot taken in the prefix.
    ///
    /// The "isInfiniteResource" check inside GameTick is
    /// already there (it's the scenario-level flag the player
    /// can set in a custom scenario). Our mod makes the
    /// restoration happen regardless of that flag, so the
    /// behaviour is consistent across all scenario types.
    ///
    /// The factory we patch is <c>GameMain.localPlanet.factory</c> -
    /// hand-mining only happens on the planet the mecha is on,
    /// which is the local planet. (We do NOT use
    /// <c>GameMain.data.mainFactory</c> - that is the home planet,
    /// which differs from the local planet during interstellar
    /// travel.)
    /// </summary>
    [HarmonyPatch(typeof(PlayerAction_Mine), nameof(PlayerAction_Mine.GameTick))]
    internal static class IcarusPatch
    {
        internal sealed class Snapshot
        {
            public int veinId;
            public int amount;
            public short groupIndex;
            public long groupAmount;
        }

        private static bool firstFireLogged = true;

        static void Prefix(PlayerAction_Mine __instance, long timei, ref Snapshot __state)
        {
            try
            {
                if (!Plugin.Config.EnableIcarusPatchFlag.Value) return;
                if (__instance == null) return;

                // GameMain.localPlanet is the planet the mecha is currently
                // on; .factory is the PlanetFactory for that planet.
                PlanetFactory factory = GameMain.localPlanet?.factory;
                if (factory == null || factory.veinPool == null || factory.veinGroups == null) return;

                int vid = __instance.miningId;
                if (vid <= 0 || vid >= factory.veinPool.Length) return;
                var vein = factory.veinPool[vid];
                if (vein.id == 0) return;

                __state = new Snapshot
                {
                    veinId = vid,
                    amount = vein.amount,
                    groupIndex = vein.groupIndex,
                };
                if (vein.groupIndex > 0 && vein.groupIndex < factory.veinGroups.Length)
                {
                    __state.groupAmount = factory.veinGroups[vein.groupIndex].amount;
                }
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch B (Icarus) prefix threw: " + ex);
            }
        }

        [HarmonyPriority(Priority.High)]
        static void Postfix(PlayerAction_Mine __instance, long timei, ref Snapshot __state)
        {
            try
            {
                if (__state == null || __state.veinId == 0) return;
                if (!Plugin.Config.EnableIcarusPatchFlag.Value) return;

                // GameMain.localPlanet is the planet the mecha is currently
                // on; .factory is the PlanetFactory for that planet.
                PlanetFactory factory = GameMain.localPlanet?.factory;
                if (factory == null || factory.veinPool == null || factory.veinGroups == null) return;

                int vid = __state.veinId;
                if (vid <= 0 || vid >= factory.veinPool.Length) return;
                if (factory.veinPool[vid].id == 0) return; // vein removed; leave removed

                if (factory.veinPool[vid].amount != __state.amount)
                {
                    factory.veinPool[vid].amount = __state.amount;
                    int gid = __state.groupIndex;
                    if (gid > 0 && gid < factory.veinGroups.Length)
                    {
                        factory.veinGroups[gid].amount = __state.groupAmount;
                    }

                    if (firstFireLogged && EndlessResourcesLog.IsDebugEnabled())
                    {
                        EndlessResourcesLog.Info("[patch] Patch B (Icarus) fired: vein " + vid + " restored to " + __state.amount);
                        firstFireLogged = false;
                    }
                }
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch B (Icarus) postfix threw: " + ex);
            }
        }
    }
}
