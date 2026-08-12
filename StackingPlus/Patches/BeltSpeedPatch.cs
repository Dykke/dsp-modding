using HarmonyLib;
using System;
using System.Collections.Generic;

namespace StackingPlus
{
    /// <summary>
    /// Optional belt-speed multiplier (default OFF).
    ///
    /// v0.1.0 attempt 1 (raw NewBeltComponent prefix) failed: DSP drives cargo
    /// movement from the cargo-PATH chunk speed, and on load paths are imported
    /// straight from the save (`CargoPath.Import`), bypassing `beltPool[].speed`.
    /// So existing belts never sped up.
    ///
    /// Correct design (this file):
    ///  1. Scale the belt ITEM PROTO speed (`ItemProto.prefabDesc.beltSpeed`) as
    ///     the single source of truth. New belts + segment sizing read it, so
    ///     everything built afterwards is consistent. Done in `GameMain.Begin`
    ///     (each game start, after protos are ready) and idempotently recomputed
    ///     from a snapshot of originals so changing the multiplier never compounds.
    ///  2. On save load, rewrite each existing belt's path segment chunk speed to
    ///     the scaled target via `CargoPath.InsertChunk` - an idempotent SET (not
    ///     a multiply), so repeated save/reload cycles do not stack the boost even
    ///     though chunk speed is serialized.
    ///
    /// Belt-speed settings are applied at game start; changing the multiplier
    /// needs a game restart (like all launch-time config here).
    /// </summary>
    internal static class BeltSpeedPatch
    {
        // protoId -> original (unscaled) prefabDesc.beltSpeed
        private static readonly Dictionary<int, int> _origBeltSpeed = new Dictionary<int, int>();
        private static bool _snapshotTaken;
        private static float _appliedMult = float.NaN;

        /// <summary>
        /// Scale every belt item proto to the configured multiplier. Idempotent:
        /// always recomputed from the snapshot of originals, guarded by a fast path.
        /// </summary>
        internal static void EnsureProtoScaled()
        {
            try
            {
                if (!ModSettings.BeltSpeedEnable) return;
                if (_snapshotTaken && _appliedMult == ModSettings.BeltSpeedMultiplier) return; // fast path

                var items = LDB.items;
                if (items == null || items.dataArray == null) return;

                float m = ModSettings.BeltSpeedMultiplier;
                int count = 0;
                foreach (var proto in items.dataArray)
                {
                    if (proto == null) continue;
                    var pd = proto.prefabDesc;
                    if (pd == null || !pd.isBelt || pd.beltSpeed <= 0) continue;

                    int orig;
                    if (!_origBeltSpeed.TryGetValue(proto.ID, out orig))
                    {
                        orig = pd.beltSpeed;
                        _origBeltSpeed[proto.ID] = orig;
                    }

                    int scaled = ScaleSpeed(orig, m);
                    StackingPlusLog.Info("[belt] proto " + proto.ID + " (" + proto.name + ") orig=" + orig
                        + " current=" + pd.beltSpeed + " -> target=" + scaled + " (x" + m + ")");
                    if (pd.beltSpeed != scaled)
                    {
                        pd.beltSpeed = scaled;
                        count++;
                    }
                }
                _snapshotTaken = true;
                _appliedMult = m;
                if (count > 0) StackingPlusLog.Info("[belt] scaled " + count + " belt proto(s) x" + m);
            }
            catch (Exception ex)
            {
                StackingPlusLog.Warn("EnsureProtoScaled failed: " + ex.Message);
            }
        }

        /// <summary>Scaled target speed for a belt item proto id.</summary>
        private static int ScaledSpeedForProto(int protoId)
        {
            int orig;
            if (_origBeltSpeed.TryGetValue(protoId, out orig))
                return ScaleSpeed(orig, ModSettings.BeltSpeedMultiplier);

            var proto = LDB.items.Select(protoId);
            if (proto != null && proto.prefabDesc != null && proto.prefabDesc.isBelt)
                return proto.prefabDesc.beltSpeed; // already scaled by EnsureProtoScaled
            return 0;
        }

        private static AccessTools.FieldRef<CargoPath, int> _chunkCountRef =
            AccessTools.FieldRefAccess<CargoPath, int>("chunkCount");

        private static int ReadChunkSpeed(CargoPath path, int index)
        {
            try
            {
                if (path == null || path.chunks == null || _chunkCountRef == null) return -1;
                int chunkCount = _chunkCountRef(path);
                for (int i = 0; i < chunkCount; i++)
                {
                    int begin = path.chunks[i * 3];
                    int len = path.chunks[i * 3 + 1];
                    if (index >= begin && index < begin + len) return path.chunks[i * 3 + 2];
                }
            }
            catch { }
            return -1;
        }

        private static int ScaleSpeed(int orig, float m)
        {
            if (m <= 1.0f) return orig;
            int s = (int)Math.Round(orig * m);
            if (s < 1) s = 1;
            if (s > 255) s = 255; // BeltComponent.speed is serialized as a byte
            return s;
        }

        /// <summary>
        /// Postfix on CargoTraffic.Import: rewrite existing belts + their path
        /// segment chunk speeds to the scaled target. Idempotent SET so repeated
        /// save/reload does not compound.
        /// </summary>
        internal static void AfterCargoTrafficImport(CargoTraffic __instance)
        {
            try
            {
                if (!ModSettings.BeltSpeedEnable) return;
                EnsureProtoScaled();

                var factory = __instance.factory;
                if (factory == null || factory.entityPool == null) return;
                if (__instance.beltPool == null || __instance.pathPool == null) return;

                int rewritten = 0;
                int sampled = 0;
                for (int k = 1; k < __instance.beltCursor; k++)
                {
                    // BeltComponent is a struct; copy for reads, write via the array.
                    BeltComponent belt = __instance.beltPool[k];
                    if (belt.id != k) continue; // recycled / empty slot

                    int entityId = belt.entityId;
                    if (entityId <= 0 || entityId >= factory.entityPool.Length) continue;

                    int protoId = factory.entityPool[entityId].protoId;
                    int scaled = ScaledSpeedForProto(protoId);
                    if (scaled <= 0) continue;

                    int pathId = belt.segPathId;
                    CargoPath path = (pathId > 0 && pathId < __instance.pathPool.Length) ? __instance.pathPool[pathId] : null;

                    // Diagnostic sample on the first few belts: what is the chunk
                    // speed before/after, and what is beltPool speed?
                    bool sample = sampled < 3;
                    int before = -1;
                    if (sample && path != null) before = ReadChunkSpeed(path, belt.segIndex);

                    if (__instance.beltPool[k].speed != scaled)
                        __instance.beltPool[k].speed = scaled;

                    if (path != null && belt.segLength > 0)
                    {
                        path.InsertChunk(belt.segIndex, belt.segLength, scaled);
                        rewritten++;
                    }

                    if (sample && path != null)
                    {
                        int after = ReadChunkSpeed(path, belt.segIndex);
                        StackingPlusLog.Info("[belt] sample belt#" + k + " proto=" + protoId
                            + " segIdx=" + belt.segIndex + " segLen=" + belt.segLength
                            + " target=" + scaled + " chunkSpeed " + before + " -> " + after
                            + " beltPoolSpeed=" + __instance.beltPool[k].speed);
                        sampled++;
                    }
                }

                if (rewritten > 0)
                {
                    string planet = (factory.planet != null) ? factory.planet.displayName : "?";
                    StackingPlusLog.Info("[belt] rewrote " + rewritten + " existing belt segment(s) on " + planet
                        + " (multiplier x" + ModSettings.BeltSpeedMultiplier + ").");
                }
            }
            catch (Exception ex)
            {
                StackingPlusLog.Error("AfterCargoTrafficImport failed: " + ex);
            }
        }
    }
}
