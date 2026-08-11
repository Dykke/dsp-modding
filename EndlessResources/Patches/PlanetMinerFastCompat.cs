using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace EndlessResources
{
    /// <summary>
    /// Compatibility layer for <c>PlanetMinerFast</c> (DysonSphereMods).
    ///
    /// <para>
    /// <b>Why this exists.</b> PlanetMinerFast bypasses
    /// <c>MinerComponent.InternalUpdate</c> entirely. It does its own
    /// vein mining in a slow-tick handler chain:
    /// </para>
    ///
    /// <code>
    /// PlanetMinerFastPlugin.OnSlowTick()        // line 93
    ///   -> ProcessFactory(PlanetFactory)        // line 103
    ///     -> TryMineVein(veinPool, idx, ...)    // line 185
    ///       -> veinPool[index].amount -= amountToConsume;      // line 197
    ///       -> factory.veinGroups[groupIdx].amount -= ...;     // line 201
    /// </code>
    ///
    /// <para>
    /// Our Patch A (<c>MinerComponent.InternalUpdate</c>) is the normal
    /// DSP path for vein depletion. PlanetMinerFast does NOT go through
    /// it - it modifies <c>veinPool[].amount</c> and
    /// <c>veinGroups[].amount</c> directly in its own slow tick. So
    /// with PlanetMinerFast installed, the station vein still depletes
    /// despite Patch A firing correctly for the drill.
    /// </para>
    ///
    /// <para>
    /// <b>The fix.</b> Detect <c>PlanetMinerFast.PlanetMinerFastPlugin</c>
    /// at runtime via reflection, and patch its <c>OnSlowTick</c> method
    /// with a snapshot (prefix) / restore (postfix) pair. The snapshot
    /// captures every <c>veinPool[].amount</c> and
    /// <c>veinGroups[].amount</c> across all factories BEFORE
    /// PlanetMinerFast runs. The restore undoes every change
    /// PlanetMinerFast made, leaving the vein amounts at their pre-call
    /// values. The station still gets the items in its storage (the
    /// decrements happened; we just undo them after the fact) - the
    /// user can mine indefinitely.
    /// </para>
    ///
    /// <para>
    /// <b>Load-order caveat.</b> BepInEx loads plugins in alphabetical
    /// order. EndlessResources ("E") loads before PlanetMinerFast
    /// ("P"), so at our <c>Awake</c> the PlanetMinerFast type isn't
    /// in any loaded assembly yet and the first detection returns
    /// null. The retry happens in <see cref="Retry"/>, called from
    /// <c>Plugin.Start()</c>. Unity guarantees every component's
    /// <c>Awake()</c> runs before any component's <c>Start()</c>, and
    /// BepInEx instantiates every plugin's component before the first
    /// frame - so by the time our <c>Start()</c> runs, PlanetMinerFast's
    /// <c>Awake()</c> has already executed and its assembly is in
    /// <see cref="AppDomain.CurrentDomain"/>. No coroutine or extra
    /// Harmony patch needed.
    /// </para>
    ///
    /// <para>
    /// <b>Cost.</b> One allocation per factory per slow tick (an
    /// <c>int[]</c> of length <c>veinPool.Length</c> and a <c>long[]</c>
    /// of length <c>veinGroups.Length</c>). The slow tick fires every
    /// few seconds (not every frame), so GC pressure is acceptable.
    /// </para>
    ///
    /// <para>
    /// <b>Detection.</b> Reflection only - we don't take a hard
    /// dependency on PlanetMinerFast. If the mod is not installed,
    /// <see cref="Apply"/> is a no-op (logs "not detected" once and
    /// returns). If the mod is installed but a future version renames
    /// <c>OnSlowTick</c>, we log a warning and skip the patch (the
    /// station will deplete as normal - degrade gracefully).
    /// </para>
    /// </summary>
    internal static class PlanetMinerFastCompat
    {
        private static bool _applied = false;
        private static bool _loggedNotDetected = false;
        private static Harmony _storedHarmony = null;

        // Per-factory snapshots, keyed by factory.index. Reset to empty
        // after every OnSlowTick. Reused across calls to amortize the
        // Dictionary allocation, but the int[]/long[] values are
        // reallocated each call (their length depends on the planet's
        // current vein count, which can change).
        private static readonly Dictionary<int, int[]> _veinSnapshots = new Dictionary<int, int[]>();
        private static readonly Dictionary<int, long[]> _groupSnapshots = new Dictionary<int, long[]>();

        /// <summary>
        /// Apply the snapshot/restore Harmony patch on PlanetMinerFast's
        /// <c>OnSlowTick</c> method, if PlanetMinerFast is loaded.
        /// Idempotent (safe to call multiple times).
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            _storedHarmony = harmony;
            TryApply();
        }

        /// <summary>
        /// Retry the compat detection. Called from <c>Plugin.Start()</c>,
        /// which Unity guarantees runs after every plugin's <c>Awake()</c>
        /// has completed.
        /// </summary>
        public static void Retry()
        {
            if (_storedHarmony == null)
            {
                EndlessResourcesLog.Warn("[compat] Retry called before Apply; skipping.");
                return;
            }
            TryApply();
        }

        private static void TryApply()
        {
            if (_applied) return;

            try
            {
                // Resolve the plugin type across all loaded assemblies.
                // PlanetMinerFast.dll may load under any assembly name;
                // we look for the type by full name.
                Type pluginType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    pluginType = asm.GetType("PlanetMinerFast.PlanetMinerFastPlugin", false);
                    if (pluginType != null) break;
                }
                if (pluginType == null)
                {
                    if (!_loggedNotDetected)
                    {
                        EndlessResourcesLog.Info("[compat] PlanetMinerFast not detected yet; will retry on Start.");
                        _loggedNotDetected = true;
                    }
                    return;
                }

                _loggedNotDetected = false;

                // OnSlowTick is private in the source. Use NonPublic to
                // reach it. The method is parameterless.
                var onSlowTick = pluginType.GetMethod(
                    "OnSlowTick",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (onSlowTick == null)
                {
                    EndlessResourcesLog.Warn("[compat] PlanetMinerFast detected but OnSlowTick method not found; skipping compat patch.");
                    return;
                }

                var prefix = typeof(PlanetMinerFastCompat).GetMethod(nameof(SnapshotPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                var postfix = typeof(PlanetMinerFastCompat).GetMethod(nameof(RestorePostfix), BindingFlags.Static | BindingFlags.NonPublic);

                _storedHarmony.Patch(onSlowTick, new HarmonyMethod(prefix), new HarmonyMethod(postfix));

                _applied = true;

                EndlessResourcesLog.Info("[compat] PlanetMinerFast detected; applied snapshot/restore patch on PlanetMinerFastPlugin.OnSlowTick.");
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("[compat] Failed to apply PlanetMinerFast compat patch: " + ex);
            }
        }

        /// <summary>
        /// Prefix on PlanetMinerFast.OnSlowTick. Captures every
        /// <c>veinPool[].amount</c> and <c>veinGroups[].amount</c>
        /// across all planets, keyed by factory.index.
        /// </summary>
        private static void SnapshotPrefix()
        {
            try
            {
                if (GameMain.data == null || GameMain.data.factories == null) return;
                _veinSnapshots.Clear();
                _groupSnapshots.Clear();
                foreach (var factory in GameMain.data.factories)
                {
                    if (factory == null || factory.veinPool == null) continue;

                    int[] veinSnap = new int[factory.veinPool.Length];
                    for (int i = 0; i < factory.veinPool.Length; i++)
                    {
                        veinSnap[i] = factory.veinPool[i].amount;
                    }
                    _veinSnapshots[factory.index] = veinSnap;

                    if (factory.veinGroups != null)
                    {
                        long[] groupSnap = new long[factory.veinGroups.Length];
                        for (int i = 0; i < factory.veinGroups.Length; i++)
                        {
                            groupSnap[i] = factory.veinGroups[i].amount;
                        }
                        _groupSnapshots[factory.index] = groupSnap;
                    }
                }
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("[compat] SnapshotPrefix threw: " + ex);
            }
        }

        /// <summary>
        /// Postfix on PlanetMinerFast.OnSlowTick. Restores every
        /// <c>veinPool[].amount</c> and <c>veinGroups[].amount</c> to
        /// the pre-call snapshot value, undoing any decrements
        /// PlanetMinerFast made. Skips removed veins (<c>id == 0</c>) -
        /// if PlanetMinerFast legitimately removed a vein, we leave it
        /// removed.
        /// </summary>
        private static void RestorePostfix()
        {
            try
            {
                if (GameMain.data == null || GameMain.data.factories == null) return;
                int nVeinRestored = 0;
                int nGroupRestored = 0;
                foreach (var factory in GameMain.data.factories)
                {
                    if (factory == null || factory.veinPool == null) continue;

                    if (_veinSnapshots.TryGetValue(factory.index, out var veinSnap)
                        && veinSnap.Length == factory.veinPool.Length)
                    {
                        for (int i = 0; i < factory.veinPool.Length; i++)
                        {
                            // Don't resurrect a vein that PlanetMinerFast
                            // legitimately removed (depleted to 0 and
                            // called RemoveVeinWithComponents). We
                            // detect this by checking id == 0 in the
                            // current pool. If the vein is still valid
                            // but the amount is different, restore.
                            if (factory.veinPool[i].id != 0
                                && factory.veinPool[i].amount != veinSnap[i])
                            {
                                factory.veinPool[i].amount = veinSnap[i];
                                nVeinRestored++;
                            }
                        }
                    }

                    if (factory.veinGroups != null
                        && _groupSnapshots.TryGetValue(factory.index, out var groupSnap)
                        && groupSnap.Length == factory.veinGroups.Length)
                    {
                        for (int i = 0; i < factory.veinGroups.Length; i++)
                        {
                            if (factory.veinGroups[i].amount != groupSnap[i])
                            {
                                factory.veinGroups[i].amount = groupSnap[i];
                                nGroupRestored++;
                            }
                        }
                    }
                }

                if ((nVeinRestored > 0 || nGroupRestored > 0) && EndlessResourcesLog.IsDebugEnabled())
                {
                    EndlessResourcesLog.Info("[compat] PlanetMinerFast restore: " + nVeinRestored + " veins, " + nGroupRestored + " groups.");
                }

                _veinSnapshots.Clear();
                _groupSnapshots.Clear();
            }
            catch (Exception ex)
            {
                EndlessResourcesLog.Error("[compat] RestorePostfix threw: " + ex);
            }
        }
    }
}
