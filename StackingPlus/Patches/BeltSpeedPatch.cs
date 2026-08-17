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
    ///
    /// 3. VISUAL FIX (2026-08-16): a boosted belt's tread animation was scrolling
    ///    at the SCALED rate, making e.g. a 2x Mk.II (visual "state"=4) read to
    ///    the eye almost like vanilla Mk.III (state=5) - confirmed via
    ///    DSPCalculatorPlus's tier diagnostic that the belt WAS genuinely item
    ///    2002 the whole time; only the tread-scroll rate was misleading.
    ///    Root cause: `CargoTraffic.SetBeltState(beltId, state)` writes `state`
    ///    straight into `BeltRenderingBatch.statePool` -> GPU `_BeltStateBuffer`,
    ///    a PURE shader/cosmetic buffer (mesh selection is separate, via
    ///    `batchIndex`, and is unaffected - confirmed correct all along). It is
    ///    only called from build tools (BuildTool*.cs - manual placement AND
    ///    blueprint paste), always passing our SCALED `beltPool[].speed` or
    ///    `handPrefabDesc.beltSpeed`. Since this buffer never touches cargo
    ///    simulation, decoupling it from the throughput multiplier is safe - it
    ///    cannot desync visible cargo from the belt. Fixed by feeding it the
    ///    ORIGINAL (unscaled) per-tier speed instead: a Harmony prefix on
    ///    SetBeltState corrects every build/paste/select call, and
    ///    AfterCargoTrafficImport explicitly re-asserts it for existing belts on
    ///    load. Throughput (chunk speed / beltPool.speed) is untouched - belts
    ///    still carry at the boosted rate; only the tread now visually matches
    ///    the belt's real tier.
    /// </summary>
    internal static class BeltSpeedPatch
    {
        // protoId -> original (unscaled) prefabDesc.beltSpeed
        private static readonly Dictionary<int, int> _origBeltSpeed = new Dictionary<int, int>();
        // scaled speed value -> original (unscaled) value. Only 3 belt tiers
        // exist, so a scaled value maps back unambiguously - this lets us
        // correct a raw "state"/"speed" VALUE with no entity/proto lookup at
        // all, which matters for build-PREVIEW belts (see BeforeSetBeltState).
        private static readonly Dictionary<int, int> _scaledToOrig = new Dictionary<int, int>();
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
                    _scaledToOrig[scaled] = orig;
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

        /// <summary>Original (unscaled) per-tier speed for a belt item proto id -
        /// used ONLY for the visual tread-rate fix, never for throughput.</summary>
        internal static int OriginalSpeedForProto(int protoId)
        {
            int orig;
            if (_origBeltSpeed.TryGetValue(protoId, out orig)) return orig;

            // Not seen yet (proto scaling hasn't run) - derive from the current
            // (possibly already-scaled) proto value so a sane fallback exists.
            var proto = LDB.items.Select(protoId);
            if (proto != null && proto.prefabDesc != null && proto.prefabDesc.isBelt)
            {
                float m = ModSettings.BeltSpeedMultiplier;
                int cur = proto.prefabDesc.beltSpeed;
                return (m > 1.0f) ? Math.Max(1, (int)Math.Round(cur / m)) : cur;
            }
            return -1; // unknown - caller should leave the value untouched
        }

        /// <summary>
        /// Prefix on CargoTraffic.SetBeltState (called from build tools - manual
        /// placement, blueprint paste, selection preview). Forces the purely-
        /// cosmetic per-node "state" to the belt's real, unscaled tier value so
        /// a boosted belt's visual identity matches its item ID. Throughput is
        /// untouched - this buffer never reaches cargo simulation.
        ///
        /// 2026-08-16 robustness fix: BuildTool_Path.UpdateGizmos (the "Construct
        /// Mode" path-drawing preview) calls this with `handPrefabDesc.beltSpeed`
        /// - the currently-held item's SCALED proto speed - for a PREVIEW/ghost
        /// belt that likely isn't a real entity yet. The original entity-based
        /// lookup (beltId -&gt; entityId -&gt; protoId) silently failed for these,
        /// leaving preview belts uncorrected even though already-placed belts
        /// were fixed. Now tries a VALUE-based reverse lookup FIRST (only 3 belt
        /// tiers exist, so a scaled value maps back to its original unambiguously
        /// - no entity needed at all), falling back to the entity-based lookup.
        /// </summary>
        internal static void BeforeSetBeltState(CargoTraffic __instance, int beltId, ref int state)
        {
            try
            {
                if (!ModSettings.BeltSpeedEnable) return;

                // Value-based (works for preview/ghost belts with no real entity).
                int orig;
                if (_scaledToOrig.TryGetValue(state, out orig) && orig != state)
                {
                    StackingPlusLog.Info("[belt][visual] SetBeltState belt#" + beltId
                        + " tread-state " + state + " -> " + orig + " (value-based; visual identity preserved).");
                    state = orig;
                    return;
                }

                // Fallback: entity-based (covers real, placed belts if the value
                // lookup ever misses - e.g. before EnsureProtoScaled has run).
                if (__instance == null || __instance.factory == null || __instance.factory.entityPool == null) return;
                if (__instance.beltPool == null || beltId <= 0 || beltId >= __instance.beltPool.Length) return;

                int entityId = __instance.beltPool[beltId].entityId;
                if (entityId <= 0 || entityId >= __instance.factory.entityPool.Length) return;

                int protoId = __instance.factory.entityPool[entityId].protoId;
                int entOrig = OriginalSpeedForProto(protoId);
                if (entOrig > 0 && state != entOrig)
                {
                    StackingPlusLog.Info("[belt][visual] SetBeltState belt#" + beltId + " proto=" + protoId
                        + " tread-state " + state + " -> " + entOrig + " (entity-based fallback).");
                    state = entOrig;
                }
            }
            catch (Exception ex)
            {
                StackingPlusLog.Warn("BeforeSetBeltState failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Postfix on CargoPath.headSpeed / CargoPath.rearSpeed getters -
        /// THIRD instance of the same bug pattern (2026-08-16, attempt 4).
        ///
        /// User reported a third symptom after the mesh-batch and tread-state
        /// fixes: the small round marker at a belt path's FIRST/LAST node still
        /// shows wrong/inconsistent coloring. Traced to `CargoTraffic
        /// .AlterPathRenderer`, which creates these markers ONLY at a path's
        /// head/rear (`!cargoPath.closed`, only where there's no further
        /// connection) and feeds them `cargoPath.headSpeed` / `.rearSpeed` -
        /// properties that are literally `chunks[2]` / `chunks[chunkCount*3-1]`,
        /// i.e. our SCALED chunk speed. Confirmed via full-codebase search:
        /// `headSpeed`/`rearSpeed` are used NOWHERE else - purely cosmetic, no
        /// gameplay logic reads them - so patching the getters themselves to
        /// report the original value is safe with zero throughput risk (unlike
        /// patching `chunks` directly, which IS live gameplay data).
        ///
        /// Reuses the same `_scaledToOrig` value-based reverse lookup as
        /// `BeforeSetBeltState` - only 3 belt tiers exist, so a scaled value
        /// maps back to its original unambiguously.
        /// </summary>
        internal static void AfterHeadOrRearSpeed(ref int __result)
        {
            try
            {
                if (!ModSettings.BeltSpeedEnable) return;
                int orig;
                if (_scaledToOrig.TryGetValue(__result, out orig) && orig != __result)
                    __result = orig;
            }
            catch (Exception ex)
            {
                StackingPlusLog.Warn("AfterHeadOrRearSpeed failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Snapshot/restore prefix+postfix on CargoTraffic.AlterBeltRenderer -
        /// THE REAL FIX (2026-08-16, attempt 2; SetBeltState above was attempt 1
        /// and is necessary but not sufficient).
        ///
        /// AlterBeltRenderer buckets `beltComponent.speed` into 3 ranges
        /// (&lt;=1 / ==2 / &gt;=3) to pick `modelBatchIndex` - one of 12
        /// `beltRenderingBatch` slots, each backed by a DIFFERENT MESH
        /// (`Configs.builtin.beltMesh[batchIndex]`). This is DSP's actual
        /// visual-tier mesh selection, and it is keyed on the belt's SPEED
        /// VALUE, not its item ID. A 2x Mk.II has speed 4, landing in the
        /// same ">=3" bucket as vanilla Mk.III's speed 5 - so a boosted Mk.II
        /// is rendered with the Mk.III MESH, not merely animated fast. This is
        /// the actual root cause of "looks like a higher tier" (SetBeltState's
        /// per-node animation-state fix, above, corrects a real but separate,
        /// secondary effect within whichever mesh batch ends up selected).
        ///
        /// Fix: temporarily present the belt's ORIGINAL (unscaled) speed via
        /// `beltPool[beltId].speed` for the DURATION of this one call only, so
        /// AlterBeltRenderer's bucket decision uses the true tier - then restore
        /// the scaled value immediately after. Order-independent: this works
        /// regardless of when/how many times the game calls AlterBeltRenderer
        /// for a given belt (build, remove+recreate, or the bulk load/"facting"
        /// pass), since every call is individually intercepted.
        /// </summary>
        internal static void BeforeAlterBeltRenderer(CargoTraffic __instance, int beltId, out int __state)
        {
            __state = -1;
            try
            {
                if (!ModSettings.BeltSpeedEnable) return;
                if (__instance == null || __instance.beltPool == null || beltId <= 0 || beltId >= __instance.beltPool.Length) return;

                BeltComponent belt = __instance.beltPool[beltId];
                if (belt.id != beltId) return;
                if (__instance.factory == null || __instance.factory.entityPool == null) return;

                int entityId = belt.entityId;
                if (entityId <= 0 || entityId >= __instance.factory.entityPool.Length) return;

                int protoId = __instance.factory.entityPool[entityId].protoId;
                int orig = OriginalSpeedForProto(protoId);
                if (orig <= 0 || orig == belt.speed) return; // nothing to correct

                __state = belt.speed;                     // remember the real (scaled) throughput speed
                __instance.beltPool[beltId].speed = orig;  // present the true tier speed for THIS call's mesh-bucket decision
            }
            catch (Exception ex)
            {
                StackingPlusLog.Warn("BeforeAlterBeltRenderer failed: " + ex.Message);
            }
        }

        internal static void AfterAlterBeltRenderer(CargoTraffic __instance, int beltId, int __state)
        {
            try
            {
                if (__state < 0) return; // prefix made no change
                if (__instance == null || __instance.beltPool == null || beltId <= 0 || beltId >= __instance.beltPool.Length) return;
                __instance.beltPool[beltId].speed = __state; // restore real throughput speed
            }
            catch (Exception ex)
            {
                StackingPlusLog.Warn("AfterAlterBeltRenderer failed: " + ex.Message);
            }
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

                    // Re-assert the visual tread rate to the real (unscaled) tier
                    // speed for this existing belt - throughput (above) stays
                    // boosted; only the cosmetic GPU buffer is corrected. Cheap:
                    // CPU-side array write only, no GPU call (Draw() uploads once
                    // per frame regardless).
                    int origVisual = OriginalSpeedForProto(protoId);
                    if (origVisual > 0)
                    {
                        try { __instance.SetBeltState(k, origVisual); }
                        catch { /* best-effort cosmetic fix */ }
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
