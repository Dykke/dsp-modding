using System;
using HarmonyLib;
using UnityEngine;

namespace PlanetwideAmmoSupply
{
    /// <summary>
    /// The refill patch. Postfix on <c>DefenseSystem.GameTick(long, bool)</c> -
    /// the per-planet defense tick, called unconditionally every game tick from
    /// <c>GameLogic.DefenseGroundSystemGameTick</c> (GameLogic.cs:1390).
    ///
    /// We do BOTH the turret and battle-base refill from here, rather than from
    /// the per-component ticks, because GameTick is a single reliable hook that
    /// exposes both component pools directly (<c>__instance.turrets</c> and
    /// <c>__instance.battleBases</c>). Turrets are actually ticked in the
    /// sibling <c>GameTick_Turret</c> (dispatched separately) - we intentionally
    /// do not depend on that path; we just need the pool + a per-tick beat.
    ///
    /// Throttled by RefillIntervalTicks. Diagnostics (gated behind DebugLog and
    /// limited to the active planet) report the scan so it is obvious whether a
    /// turret was full, below capacity, refilled, or had no stock to pull.
    /// </summary>
    [HarmonyPatch(typeof(DefenseSystem), "GameTick", new Type[] { typeof(long), typeof(bool) })]
    internal static class DefenseSystemSupplyPatch
    {
        // Matches the vanilla BeltUpdate "itemCount < 5" top-up threshold.
        private const int TurretAmmoTarget = 5;

        static void Postfix(DefenseSystem __instance, long tick, bool isActive)
        {
            try
            {
                if (!Plugin.Enabled.Value || __instance == null) return;

                int interval = Plugin.RefillIntervalTicks.Value;
                if (interval < 1) interval = 1;
                if (tick % interval != 0) return;

                PlanetFactory factory = __instance.factory;
                if (factory == null) return;

                float radius = Plugin.SupplyRadius.Value;
                // Only log on the planet the player is on, to keep it readable.
                bool debug = isActive && PlanetwideAmmoSupplyLog.IsDebugEnabled();

                if (Plugin.SupplyTurrets.Value)
                    RefillTurrets(__instance, factory, radius, debug);

                if (Plugin.SupplyBattleBases.Value)
                    RefillBattleBases(__instance, factory, radius, debug);
            }
            catch (Exception ex)
            {
                PlanetwideAmmoSupplyLog.Error("[patch] DefenseSystem.GameTick postfix threw: " + ex);
            }
        }

        private static void RefillTurrets(DefenseSystem def, PlanetFactory factory, float radius, bool debug)
        {
            var pool = def.turrets;
            if (pool == null || pool.buffer == null) return;

            TurretComponent[] buffer = pool.buffer;
            int cursor = pool.cursor;
            EntityData[] entityPool = factory.entityPool;

            int total = 0, belowCap = 0, refilled = 0, movedItems = 0, noStock = 0;

            for (int i = 1; i < cursor; i++)
            {
                // Index the array element directly - a struct copy would discard writes.
                if (buffer[i].id != i) continue;
                total++;
                if (buffer[i].itemCount >= TurretAmmoTarget) continue;
                belowCap++;

                Vector3 pos = Vector3.zero;
                int eid = buffer[i].entityId;
                if (entityPool != null && eid > 0 && eid < entityPool.Length) pos = entityPool[eid].pos;

                int itemId = buffer[i].itemId;
                if (itemId == 0)
                {
                    itemId = AmmoSourcing.FindAvailableAmmo(factory, buffer[i].ammoType, pos, radius);
                    if (itemId == 0) { noStock++; continue; }
                }

                int want = TurretAmmoTarget - buffer[i].itemCount;
                int incMoved;
                int moved = AmmoSourcing.TryPullFromPlanet(factory, itemId, want, pos, radius, out incMoved);
                if (moved <= 0) { noStock++; continue; }

                short before = buffer[i].itemCount;
                if (buffer[i].itemId == 0)
                    buffer[i].SetNewItem(itemId, (short)moved, (short)incMoved);
                else
                {
                    buffer[i].itemCount += (short)moved;
                    buffer[i].itemInc += (short)incMoved;
                }

                refilled++;
                movedItems += moved;
                if (debug && refilled <= 3)
                    PlanetwideAmmoSupplyLog.Info("[supply] turret " + i + " item " + itemId
                        + " itemCount " + before + "->" + buffer[i].itemCount);
            }

            if (debug && total > 0)
                PlanetwideAmmoSupplyLog.Info("[turret-scan] planet=" + factory.planetId
                    + " total=" + total + " belowCap=" + belowCap + " refilled=" + refilled
                    + " movedItems=" + movedItems + " noStock=" + noStock);
        }

        private static void RefillBattleBases(DefenseSystem def, PlanetFactory factory, float radius, bool debug)
        {
            var pool = def.battleBases;
            if (pool == null || pool.buffer == null) return;

            BattleBaseComponent[] buffer = pool.buffer;
            int cursor = pool.cursor;
            EntityData[] entityPool = factory.entityPool;
            string filter = Plugin.FighterItemFilter.Value;
            bool hasFilter = !string.IsNullOrEmpty(filter);

            int bases = 0, slotsRefilled = 0, movedItems = 0;

            for (int l = 1; l < cursor; l++)
            {
                BattleBaseComponent bb = buffer[l];
                if (bb == null || bb.id != l) continue;
                StorageComponent storage = bb.storage;
                if (storage == null || storage.grids == null) continue;
                bases++;

                Vector3 pos = Vector3.zero;
                int beid = bb.entityId;
                if (entityPool != null && beid > 0 && beid < entityPool.Length) pos = entityPool[beid].pos;

                StorageComponent.GRID[] grids = storage.grids;
                for (int g = 0; g < grids.Length; g++)
                {
                    int itemId = grids[g].itemId;
                    if (itemId <= 0) continue;
                    int stackSize = grids[g].stackSize;
                    if (stackSize <= 0 || grids[g].count >= stackSize) continue;
                    if (hasFilter && filter.IndexOf(itemId.ToString(), StringComparison.Ordinal) < 0) continue;

                    // Bound by this slot's room so AddItem accepts all of it (no ammo lost).
                    int want = stackSize - grids[g].count;
                    int incMoved;
                    int moved = AmmoSourcing.TryPullFromPlanet(factory, itemId, want, pos, radius, out incMoved);
                    if (moved <= 0) continue;

                    int remainInc;
                    int accepted = storage.AddItem(itemId, moved, incMoved, out remainInc, false);
                    slotsRefilled++;
                    movedItems += accepted;
                }
            }

            if (debug && bases > 0 && movedItems > 0)
                PlanetwideAmmoSupplyLog.Info("[battlebase-scan] planet=" + factory.planetId
                    + " bases=" + bases + " slotsRefilled=" + slotsRefilled + " movedItems=" + movedItems);
        }
    }
}
