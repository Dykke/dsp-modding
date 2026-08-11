using System;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace EndlessResources
{
    /// <summary>
    /// EndlessResources - BepInEx entry point.
    ///
    /// All planet vein sources (ore, oil, Icarus hand-mine,
    /// ILS / PLS vein collector) stop consuming the source's
    /// amount, and the source ILS / PLS station's storage
    /// buffer is restored after each dispatch, so the station
    /// can ship indefinitely.
    ///
    /// Each feature is gated by a config toggle in
    /// <see cref="PluginConfig"/>. See the README.md or the
    /// auto-generated BepInEx\config\com.author.EndlessResources.cfg
    /// for details.
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInProcess("DSPGAME.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        // ----- mod identity -------------------------------------------------
        public const string GUID = "com.author.EndlessResources"; // TODO: replace `author` with the real namespace before publish
        public const string NAME = "EndlessResources";
        public const string VERSION = "1.0.0";

        // ----- shared state ------------------------------------------------
        // 'new' because BaseUnityPlugin.Config is the inherited BepInEx
        // ConfigFile property; our typed wrapper has the same name.
        internal static new PluginConfig Config;
        private static Harmony _harmony;

        // ----- lifecycle ---------------------------------------------------
        private void Awake()
        {
            // 1. Read config. base.Config is the inherited BepInEx ConfigFile;
            //    Config (the field below) is our typed wrapper.
            Config = new PluginConfig(base.Config);

            // 2. Wire the log helper BEFORE any patch can log.
            EndlessResourcesLog.Init(Logger, Config.DebugLog);

            EndlessResourcesLog.Info("[config] Loaded with config: miner=" + Config.EnableMinerPatchFlag.Value
                + ", oil=" + Config.EnableOilPatchFlag.Value
                + ", icarus=" + Config.EnableIcarusPatchFlag.Value
                + ", ils_vein=" + Config.EnableILSVeinCollectionFlag.Value
                + ", ils_source=" + Config.EnableILSSourceFlag.Value
                + ", planetminerfast=" + Config.EnablePlanetMinerFastCompatFlag.Value
                + ", debug=" + Config.DebugLog.Value);

            // 3. Apply Harmony patches. PatchAll auto-discovers every
            //    [HarmonyPatch] class in the assembly. To allow graceful
            //    skip on missing methods (if a future DSP version renames
            //    a target), we PatchAll inside a try / catch and log the
            //    total applied count.
            try
            {
                _harmony = new Harmony(GUID);
                _harmony.PatchAll(typeof(Plugin).Assembly);

                int n = 0;
                foreach (var t in typeof(Plugin).Assembly.GetTypes())
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                        n++;
                }
                EndlessResourcesLog.Info("[patch] Applied " + n + " Harmony patches: MinerPatch, IcarusPatch, StationVeinCollectionPatch, StationDispatchPatch.");

                // 3a. Compat layer for PlanetMinerFast. Detected via
                //     reflection; no-op if not installed. See
                //     Patches\PlanetMinerFastCompat.cs for rationale.
                if (Config.EnablePlanetMinerFastCompatFlag.Value)
                {
                    PlanetMinerFastCompat.Apply(_harmony);
                }
            }
            catch (System.Exception ex)
            {
                EndlessResourcesLog.Error("Harmony PatchAll failed: " + ex);
            }

            // 4. DIAGNOSTIC: log the actual parameter list of each target
            //    method. This is to confirm whether the signatures we
            //    declared in our patches match the real DSP method
            //    signatures. If a parameter type is wrong, Harmony
            //    silently no-ops the patch and the postfix never fires.
            //    Output is gated by DebugLog so it doesn't spam normal
            //    sessions.
            DumpTargetMethodSignatures();
        }

        private static void DumpTargetMethodSignatures()
        {
            string[][] targets = {
                new[] { "MinerComponent",         "InternalUpdate" },
                new[] { "PlayerAction_Mine",      "GameTick" },
                new[] { "StationComponent",       "UpdateVeinCollection" },
                new[] { "StationComponent",       "DetermineDispatch" },
            };
            foreach (var t in targets)
            {
                try
                {
                    var type = AccessTools.TypeByName(t[0]);
                    if (type == null) { EndlessResourcesLog.Warn("[diag] Type not found: " + t[0]); continue; }
                    var method = AccessTools.Method(type, t[1]);
                    if (method == null) { EndlessResourcesLog.Warn("[diag] Method not found: " + t[0] + "." + t[1]); continue; }
                    var ps = method.GetParameters();
                    var sb = new StringBuilder();
                    sb.Append("[diag] ").Append(t[0]).Append(".").Append(t[1]).Append("(");
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ps[i].ParameterType.Name).Append(" ").Append(ps[i].Name);
                    }
                    sb.Append(")");
                    EndlessResourcesLog.Info(sb.ToString());
                }
                catch (Exception ex)
                {
                    EndlessResourcesLog.Error("[diag] Failed to inspect " + t[0] + "." + t[1] + ": " + ex.Message);
                }
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
