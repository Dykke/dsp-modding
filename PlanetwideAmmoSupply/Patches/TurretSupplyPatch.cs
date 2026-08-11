using System;
using HarmonyLib;
using UnityEngine;

namespace PlanetwideAmmoSupply
{
    /// <summary>
    /// Patch A - postfix on <c>DefenseSystem.GameTick_Turret(long)</c>.
    ///
    /// After the vanilla turret tick, top up each turret's ammo from the
    /// planet's logistics network (consume model), so ammo no longer has to be
    /// belted to every turret. Throttled by RefillIntervalTicks.
    ///
    /// Turret ammo model (from decompile): <c>itemCount</c> = ammo ITEMS held;
    /// the belt path (<c>BeltUpdate</c>) tops up only while <c>itemCount &lt; 5</c>,
    /// so 5 is the natural target. <c>SetNewItem(itemId,count,inc)</c> loads a
    /// fresh ammo (and sets itemBulletCount/bulletDamage); topping the same
    /// item mirrors BeltUpdate (<c>itemCount += n; itemInc += inc</c>).
    ///
    /// Postfix runs at High priority so our top-up is the final word after any
    /// other mod that also touches the turret pool (workspace convention).
    /// </summary>
    [HarmonyPatch(typeof(DefenseSystem), "GameTick_Turret")]
    internal static class TurretSupplyPatch
    {
        // Matches the vanilla BeltUpdate "< 5" top-up threshold.
        private const int TurretAmmoTarget = 5;

        private static bool firstFireLogged;

        [HarmonyPriority(Priority.High)]
        static void Postfix(DefenseSystem __instance, long tick)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.SupplyTurrets.Value) return;

                int interval = Plugin.RefillIntervalTicks.Value;
                if (interval < 1) interval = 1;
                if (tick % interval != 0) return;

                if (__instance == null) return;
                PlanetFactory factory = __instance.factory;
                if (factory == null) return;

                var pool = __instance.turrets;
                if (pool == null || pool.buffer == null) return;

                float radius = Plugin.SupplyRadius.Value;
                EntityData[] entityPool = factory.entityPool;
                TurretComponent[] buffer = pool.buffer;
                int cursor = pool.cursor;

                for (int i = 1; i < cursor; i++)
                {
                    // Array-element access keeps writes on the real struct (a
                    // local copy would silently discard them).
                    if (buffer[i].id != i) continue;
                    if (buffer[i].itemCount >= TurretAmmoTarget) continue;

                    Vector3 pos = Vector3.zero;
                    int eid = buffer[i].entityId;
                    if (entityPool != null && eid > 0 && eid < entityPool.Length) pos = entityPool[eid].pos;

                    int itemId = buffer[i].itemId;
                    if (itemId == 0)
                    {
                        // Empty turret: pick a compatible ammo the network has.
                        itemId = AmmoSourcing.FindAvailableAmmo(factory, buffer[i].ammoType, pos, radius);
                        if (itemId == 0) continue;
                    }

                    int want = TurretAmmoTarget - buffer[i].itemCount;
                    int incMoved;
                    int moved = AmmoSourcing.TryPullFromPlanet(factory, itemId, want, pos, radius, out incMoved);
                    if (moved <= 0) continue;

                    if (buffer[i].itemId == 0)
                    {
                        buffer[i].SetNewItem(itemId, (short)moved, (short)incMoved);
                    }
                    else
                    {
                        buffer[i].itemCount += (short)moved;
                        buffer[i].itemInc += (short)incMoved;
                    }

                    if (!firstFireLogged && PlanetwideAmmoSupplyLog.IsDebugEnabled())
                    {
                        PlanetwideAmmoSupplyLog.Info("[supply] turret " + i + " +" + moved
                            + " of item " + itemId + " (itemCount now " + buffer[i].itemCount + ")");
                        firstFireLogged = true;
                    }
                }
            }
            catch (Exception ex)
            {
                PlanetwideAmmoSupplyLog.Error("[patch] TurretSupply postfix threw: " + ex);
            }
        }
    }
}
