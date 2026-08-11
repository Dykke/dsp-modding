using System;
using HarmonyLib;

namespace EndlessResources
{
    /// <summary>
    /// Patch A - <c>MinerComponent.InternalUpdate</c>.
    ///
    /// Restores the planet vein's amount after the miner extracts.
    /// This covers three different "miner" types in DSP:
    ///
    /// 1. The standard mining drill placed on a planet vein
    ///    (type = EMinerType.Vein). Gated by EnableMinerPatchFlag.
    /// 2. The crude oil extractor placed on a planet oil seep
    ///    (type = EMinerType.Oil). Gated by EnableOilPatchFlag.
    /// 3. The ILS / PLS vein collector station, which uses a
    ///    MinerComponent internally to pull from the vein.
    ///    Gated by EnableMinerPatchFlag (same toggle as the
    ///    standard miner).
    ///
    /// The water extractor (type = EMinerType.Water) is NOT
    /// covered - it draws from the planet's water pool, not a
    /// finite vein, so it cannot deplete.
    ///
    /// Approach: snapshot each vein's amount + group amount in
    /// a Prefix; restore them in a Postfix. If the function
    /// decided to remove the vein (decremented to 0), the
    /// postfix sees vein.id == 0 and skips restoration. This
    /// means an over-mined vein at the moment of install will
    /// NOT be resurrected; only the future depletion is
    /// prevented (this is the right behaviour for the
    /// "mid-save upgrade" scenario).
    /// </summary>
    [HarmonyPatch(typeof(MinerComponent), nameof(MinerComponent.InternalUpdate))]
    internal static class MinerPatch
    {
        // Per-call snapshot. Class type (not struct) so Harmony's
        // __state copy is just a reference and Prefix / Postfix
        // see the same object.
        internal sealed class Snapshot
        {
            public int[] veinIds;          // miner.veins[i], or 0 if invalid
            public int[] amounts;          // factory.veinPool[veinIds[i]].amount at prefix time
            public short[] groupIndices;   // factory.veinPool[veinIds[i]].groupIndex at prefix time
            public long[] groupAmounts;    // factory.veinGroups[groupIndices[i]].amount at prefix time
        }

        // Patch A is the most-fired patch in the mod (per-tick per-miner).
        // Log every fire when DebugLog is on so we can confirm coverage
        // for both regular drills AND station-internal miners (which is
        // the scenario that the in-game test surfaced as "station still
        // reduces the vein" after the signature fix). Cheap: a single
        // LogInfo per call, gated by the existing Debug flag.

        [HarmonyPriority(Priority.VeryHigh)] // bumped from High; must run AFTER PlanetwideMining + DSP itself
        static void Prefix(
            MinerComponent __instance,
            PlanetFactory factory,
            VeinData[] veinPool,
            float power,
            float miningRate,
            float miningSpeed,
            int[] productRegister,
            ref Snapshot __state)
        {
            try
            {
                // NOTE: MinerComponent is a STRUCT in DSP, not a class.
                // Harmony passes the actual struct by value to Prefix/Postfix,
                // and we never need a null check on it (Harmony guarantees a
                // value is delivered whenever the host function is called).
                if (__instance.veins == null || __instance.veins.Length == 0) return;
                if (factory == null || factory.veinPool == null || factory.veinGroups == null) return;

                // Gate by miner type. Water is not gated (it's
                // always-on for safety, but Water doesn't decrement
                // veins anyway).
                bool isOil = __instance.type == EMinerType.Oil;
                if (isOil && !Plugin.Config.EnableOilPatchFlag.Value) return;
                if (!isOil && !Plugin.Config.EnableMinerPatchFlag.Value) return;

                int n = __instance.veins.Length;
                __state = new Snapshot
                {
                    veinIds = new int[n],
                    amounts = new int[n],
                    groupIndices = new short[n],
                    groupAmounts = new long[n],
                };

                for (int i = 0; i < n; i++)
                {
                    int vid = __instance.veins[i];
                    if (vid <= 0 || vid >= factory.veinPool.Length) continue;
                    var vein = factory.veinPool[vid];
                    if (vein.id == 0) continue; // vein already removed

                    __state.veinIds[i] = vid;
                    __state.amounts[i] = vein.amount;
                    __state.groupIndices[i] = vein.groupIndex;
                    if (vein.groupIndex > 0 && vein.groupIndex < factory.veinGroups.Length)
                    {
                        __state.groupAmounts[i] = factory.veinGroups[vein.groupIndex].amount;
                    }
                }
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch A (Miner) prefix threw: " + ex);
            }
        }

        [HarmonyPriority(Priority.VeryHigh)] // bumped from High; must run AFTER PlanetwideMining + DSP itself
        static void Postfix(
            MinerComponent __instance,
            PlanetFactory factory,
            VeinData[] veinPool,
            float power,
            float miningRate,
            float miningSpeed,
            int[] productRegister,
            ref Snapshot __state)
        {
            try
            {
                if (__state == null || __state.veinIds == null) return;
                if (factory == null || factory.veinPool == null || factory.veinGroups == null) return;

                int nRestored = 0;
                for (int i = 0; i < __state.veinIds.Length; i++)
                {
                    int vid = __state.veinIds[i];
                    if (vid <= 0 || vid >= factory.veinPool.Length) continue;
                    if (factory.veinPool[vid].id == 0) continue; // vein was removed; leave it removed

                    // UNCONDITIONAL restore. We always set the amount back
                    // to the pre-call snapshot, regardless of whether it
                    // changed. This handles the "station still reduces"
                    // case where another mod (PlanetwideMining) or DSP
                    // itself has a postfix that runs after ours and
                    // decrements the vein a second time. The mod's whole
                    // purpose is infinite resources, so a redundant write
                    // is the correct behaviour.
                    factory.veinPool[vid].amount = __state.amounts[i];

                    int gid = __state.groupIndices[i];
                    if (gid > 0 && gid < factory.veinGroups.Length)
                    {
                        factory.veinGroups[gid].amount = __state.groupAmounts[i];
                    }

                    nRestored++;
                }

                // Log every fire so we can see what types and how many
                // veins this call covered. The previous one-shot gate
                // hid the per-tick picture needed to diagnose "station
                // still reduces" - the first fire logged (drill) and
                // all subsequent fires (including station's miner) were
                // invisible. Gated by DebugLog so production is quiet.
                if (nRestored > 0 && EndlessResourcesLog.IsDebugEnabled())
                {
                    // Surface the first non-zero vein ID so we can see
                    // whether the station cycles through different veins
                    // (firstVid changes each call) or always mines the
                    // same one (firstVid stable).
                    int firstVid = 0;
                    int firstAmt = 0;
                    for (int i = 0; i < __state.veinIds.Length; i++)
                    {
                        if (__state.veinIds[i] > 0)
                        {
                            firstVid = __state.veinIds[i];
                            firstAmt = __state.amounts[i];
                            break;
                        }
                    }
                    EndlessResourcesLog.Info("[patch] Patch A fired: type=" + __instance.type
                        + ", miner.veins.len=" + __state.veinIds.Length
                        + ", restored=" + nRestored
                        + ", firstVid=" + firstVid
                        + ", firstAmt=" + firstAmt);
                }
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("Patch A (Miner) postfix threw: " + ex);
            }
        }
    }
}
