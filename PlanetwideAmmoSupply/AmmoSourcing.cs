using UnityEngine;

namespace PlanetwideAmmoSupply
{
    /// <summary>
    /// Shared logistics-sourcing helper. Reads the planet's station network
    /// (ILS/PLS) and pulls items into combat structures, decrementing the
    /// source station stock in place (consume model). Used by both the turret
    /// and battle-base patches. All calls happen on the game-tick thread.
    /// </summary>
    internal static class AmmoSourcing
    {
        /// <summary>
        /// Pull up to <paramref name="want"/> of <paramref name="itemId"/> from
        /// logistics stations on the planet, honoring SupplyRadius and
        /// RequireStationSupplyFlag. Returns the count moved and sets
        /// <paramref name="incMoved"/> (proportional proliferator points),
        /// mirroring the game's own proportional-inc math. Decrements the
        /// source <see cref="StationStore"/> in place.
        /// </summary>
        internal static int TryPullFromPlanet(PlanetFactory factory, int itemId, int want, Vector3 pos, float radius, out int incMoved)
        {
            incMoved = 0;
            if (factory == null || itemId <= 0 || want <= 0) return 0;

            PlanetTransport transport = factory.transport;
            if (transport == null || transport.stationPool == null) return 0;

            bool requireSupply = Plugin.RequireStationSupplyFlag.Value;
            float radiusSqr = (radius > 0f) ? radius * radius : 0f;
            EntityData[] entityPool = factory.entityPool;
            int cursor = transport.stationCursor;
            int moved = 0;

            for (int s = 1; s < cursor && moved < want; s++)
            {
                StationComponent station = transport.stationPool[s];
                if (station == null || station.id != s || station.storage == null) continue;

                if (radiusSqr > 0f && entityPool != null)
                {
                    int eid = station.entityId;
                    if (eid > 0 && eid < entityPool.Length
                        && (entityPool[eid].pos - pos).sqrMagnitude > radiusSqr) continue;
                }

                StationStore[] storage = station.storage;
                for (int j = 0; j < storage.Length && moved < want; j++)
                {
                    if (storage[j].itemId != itemId || storage[j].count <= 0) continue;
                    if (requireSupply
                        && storage[j].localLogic != ELogisticStorage.Supply
                        && storage[j].remoteLogic != ELogisticStorage.Supply) continue;

                    int take = want - moved;
                    if (take > storage[j].count) take = storage[j].count;

                    int incTake = 0;
                    if (storage[j].inc > 0)
                    {
                        incTake = (int)((float)storage[j].inc * take / storage[j].count + 0.5f);
                        if (incTake > storage[j].inc) incTake = storage[j].inc;
                    }

                    storage[j].count -= take;
                    storage[j].inc -= incTake;
                    moved += take;
                    incMoved += incTake;
                }
            }
            return moved;
        }

        /// <summary>
        /// Find a compatible ammo item id for <paramref name="ammoType"/> that
        /// currently has stock in the planet's logistics network (respecting
        /// radius). Returns 0 if none. Used to fill an empty turret whose ammo
        /// item is not yet chosen. Compatible items come from
        /// <c>ItemProto.turretNeeds[ammoType]</c> (all ammo tiers of that type).
        /// </summary>
        internal static int FindAvailableAmmo(PlanetFactory factory, EAmmoType ammoType, Vector3 pos, float radius)
        {
            int idx = (int)ammoType;
            if (idx <= 0 || ItemProto.turretNeeds == null || idx >= ItemProto.turretNeeds.Length) return 0;
            int[] needs = ItemProto.turretNeeds[idx];
            if (needs == null) return 0;

            // needs[] is built by ItemProto.InitTurretNeeds in item-id order
            // (ascending), zero-padded at the tail. For DSP ammo, higher id ==
            // higher tier, so "prefer highest" scans from the end backward and
            // "prefer lowest/cheapest" scans forward.
            if (Plugin.PreferHighestAmmoTier.Value)
            {
                for (int n = needs.Length - 1; n >= 0; n--)
                {
                    int candidate = needs[n];
                    if (candidate > 0 && HasStock(factory, candidate, pos, radius)) return candidate;
                }
            }
            else
            {
                for (int n = 0; n < needs.Length; n++)
                {
                    int candidate = needs[n];
                    if (candidate > 0 && HasStock(factory, candidate, pos, radius)) return candidate;
                }
            }
            return 0;
        }

        private static bool HasStock(PlanetFactory factory, int itemId, Vector3 pos, float radius)
        {
            PlanetTransport transport = factory.transport;
            if (transport == null || transport.stationPool == null) return false;

            bool requireSupply = Plugin.RequireStationSupplyFlag.Value;
            float radiusSqr = (radius > 0f) ? radius * radius : 0f;
            EntityData[] entityPool = factory.entityPool;
            int cursor = transport.stationCursor;

            for (int s = 1; s < cursor; s++)
            {
                StationComponent station = transport.stationPool[s];
                if (station == null || station.id != s || station.storage == null) continue;
                if (radiusSqr > 0f && entityPool != null)
                {
                    int eid = station.entityId;
                    if (eid > 0 && eid < entityPool.Length
                        && (entityPool[eid].pos - pos).sqrMagnitude > radiusSqr) continue;
                }
                StationStore[] storage = station.storage;
                for (int j = 0; j < storage.Length; j++)
                {
                    if (storage[j].itemId != itemId || storage[j].count <= 0) continue;
                    if (requireSupply
                        && storage[j].localLogic != ELogisticStorage.Supply
                        && storage[j].remoteLogic != ELogisticStorage.Supply) continue;
                    return true;
                }
            }
            return false;
        }
    }
}
