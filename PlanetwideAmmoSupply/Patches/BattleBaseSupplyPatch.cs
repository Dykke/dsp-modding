using System;
using HarmonyLib;
using UnityEngine;

namespace PlanetwideAmmoSupply
{
    /// <summary>
    /// Patch B - postfix on
    /// <c>BattleBaseComponent.InternalUpdate(float, PlanetFactory, float, ref AnimData)</c>.
    ///
    /// Keeps a battle base's storage (ammo + fighters) topped up from the
    /// planet's logistics network. Only refills grid slots that already hold an
    /// item, up to that slot's stack size, so the base is stocked with exactly
    /// what the player configured it to use (its drones then relay ammo to
    /// turrets in range). Throttled by RefillIntervalTicks.
    ///
    /// BattleBaseComponent is a class (ObjectPool), so the postfix instance is a
    /// live reference - no struct write-back dance needed.
    /// </summary>
    [HarmonyPatch(typeof(BattleBaseComponent), "InternalUpdate")]
    internal static class BattleBaseSupplyPatch
    {
        private static bool firstFireLogged;

        [HarmonyPriority(Priority.High)]
        static void Postfix(BattleBaseComponent __instance, PlanetFactory factory)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.SupplyBattleBases.Value) return;
                if (__instance == null || factory == null) return;

                int interval = Plugin.RefillIntervalTicks.Value;
                if (interval < 1) interval = 1;
                if (GameMain.gameTick % interval != 0) return;

                StorageComponent storage = __instance.storage;
                if (storage == null || storage.grids == null) return;

                float radius = Plugin.SupplyRadius.Value;
                EntityData[] entityPool = factory.entityPool;
                Vector3 pos = Vector3.zero;
                int beid = __instance.entityId;
                if (entityPool != null && beid > 0 && beid < entityPool.Length) pos = entityPool[beid].pos;

                string filter = Plugin.FighterItemFilter.Value;
                bool hasFilter = !string.IsNullOrEmpty(filter);

                StorageComponent.GRID[] grids = storage.grids;
                for (int g = 0; g < grids.Length; g++)
                {
                    int itemId = grids[g].itemId;
                    if (itemId <= 0) continue;

                    int stackSize = grids[g].stackSize;
                    if (stackSize <= 0 || grids[g].count >= stackSize) continue;

                    if (hasFilter && filter.IndexOf(itemId.ToString(), StringComparison.Ordinal) < 0) continue;

                    // Bound the pull by this slot's remaining room so AddItem
                    // accepts all of it (no items are lost after leaving the
                    // station).
                    int want = stackSize - grids[g].count;
                    int incMoved;
                    int moved = AmmoSourcing.TryPullFromPlanet(factory, itemId, want, pos, radius, out incMoved);
                    if (moved <= 0) continue;

                    int remainInc;
                    int accepted = storage.AddItem(itemId, moved, incMoved, out remainInc, false);
                    if (accepted < moved)
                    {
                        // Should not happen (want <= slot room), but never lose ammo:
                        PlanetwideAmmoSupplyLog.Warn("[supply] battlebase " + beid + " could not store "
                            + (moved - accepted) + " of item " + itemId + " (storage unexpectedly full)");
                    }

                    if (!firstFireLogged && PlanetwideAmmoSupplyLog.IsDebugEnabled())
                    {
                        PlanetwideAmmoSupplyLog.Info("[supply] battlebase " + beid + " +" + accepted
                            + " of item " + itemId);
                        firstFireLogged = true;
                    }
                }
            }
            catch (Exception ex)
            {
                PlanetwideAmmoSupplyLog.Error("[patch] BattleBaseSupply postfix threw: " + ex);
            }
        }
    }
}
