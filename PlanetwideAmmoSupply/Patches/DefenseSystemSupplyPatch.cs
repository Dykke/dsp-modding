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
    /// We do BOTH the turret and battle-base refill from here because GameTick
    /// is a single reliable hook that exposes both component pools directly
    /// (<c>__instance.turrets</c> / <c>__instance.battleBases</c>). Turrets are
    /// actually ticked in the sibling <c>GameTick_Turret</c> (dispatched
    /// separately) - we intentionally do not depend on that path.
    ///
    /// Throttled by RefillIntervalTicks. Logging (gated behind DebugLog, active
    /// planet only) is a single aggregate line, further throttled to ~1 per
    /// LogThrottleTicks and emitted only when ammo actually moved or a turret
    /// needed ammo none was available for - so a steady, topped-up base is
    /// silent.
    /// </summary>
    [HarmonyPatch(typeof(DefenseSystem), "GameTick", new Type[] { typeof(long), typeof(bool) })]
    internal static class DefenseSystemSupplyPatch
    {
        // Matches the vanilla BeltUpdate "itemCount < 5" top-up threshold.
        private const int TurretAmmoTarget = 5;

        // Minimum ticks between aggregate log lines (~5s at 60 UPS).
        private const long LogThrottleTicks = 300L;
        private static long _lastLogTick = long.MinValue;

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

                int tRefilled = 0, tMoved = 0, tNoStock = 0, bbMoved = 0;

                if (Plugin.SupplyTurrets.Value)
                    RefillTurrets(__instance, factory, radius, out tRefilled, out tMoved, out tNoStock);

                if (Plugin.SupplyBattleBases.Value)
                    RefillBattleBases(__instance, factory, radius, out bbMoved);

                // One throttled aggregate line, active planet only, only when
                // something happened. Steady state is silent.
                if (isActive && PlanetwideAmmoSupplyLog.IsDebugEnabled()
                    && (tMoved > 0 || bbMoved > 0 || tNoStock > 0)
                    && tick - _lastLogTick >= LogThrottleTicks)
                {
                    _lastLogTick = tick;
                    string msg = "[supply] planet=" + factory.planetId
                        + " turrets{refilled=" + tRefilled + " items=" + tMoved
                        + (tNoStock > 0 ? " noStock=" + tNoStock : "") + "}";
                    if (bbMoved > 0) msg += " battlebase{items=" + bbMoved + "}";
                    PlanetwideAmmoSupplyLog.Info(msg);
                }
            }
            catch (Exception ex)
            {
                PlanetwideAmmoSupplyLog.Error("[patch] DefenseSystem.GameTick postfix threw: " + ex);
            }
        }

        private static void RefillTurrets(DefenseSystem def, PlanetFactory factory, float radius,
            out int refilled, out int movedItems, out int noStock)
        {
            refilled = 0; movedItems = 0; noStock = 0;

            var pool = def.turrets;
            if (pool == null || pool.buffer == null) return;

            TurretComponent[] buffer = pool.buffer;
            int cursor = pool.cursor;
            EntityData[] entityPool = factory.entityPool;

            for (int i = 1; i < cursor; i++)
            {
                // Index the array element directly - a struct copy would discard writes.
                if (buffer[i].id != i) continue;
                if (buffer[i].itemCount >= TurretAmmoTarget) continue;

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

                if (buffer[i].itemId == 0)
                    buffer[i].SetNewItem(itemId, (short)moved, (short)incMoved);
                else
                {
                    buffer[i].itemCount += (short)moved;
                    buffer[i].itemInc += (short)incMoved;
                }

                refilled++;
                movedItems += moved;
            }
        }

        private static void RefillBattleBases(DefenseSystem def, PlanetFactory factory, float radius, out int movedItems)
        {
            movedItems = 0;

            var pool = def.battleBases;
            if (pool == null || pool.buffer == null) return;

            BattleBaseComponent[] buffer = pool.buffer;
            int cursor = pool.cursor;
            EntityData[] entityPool = factory.entityPool;
            string filter = Plugin.FighterItemFilter.Value;
            bool hasFilter = !string.IsNullOrEmpty(filter);

            for (int l = 1; l < cursor; l++)
            {
                BattleBaseComponent bb = buffer[l];
                if (bb == null || bb.id != l) continue;
                StorageComponent storage = bb.storage;
                if (storage == null || storage.grids == null) continue;

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
                    movedItems += accepted;
                }
            }
        }
    }
}
