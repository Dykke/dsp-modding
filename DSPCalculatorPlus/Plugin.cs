using System;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace DSPCalculatorPlus
{
    /// <summary>
    /// DSPCalculatorPlus - BepInEx entry point.
    ///
    /// A Harmony-patch companion for DSPCalculator
    /// (<c>com.GniMaerd.DSPCalculator</c>, Thunderstore
    /// <c>jinxOAO-DSPCalculator</c>, target 0.5.25) that:
    ///   1. lets the user force a specific belt / sorter tier in
    ///      generated blueprints (DSPCalculator itself only offers a
    ///      "highest vs cheapest" boolean); and
    ///   2. fixes blueprint generation failing outright for
    ///      high-throughput items by splitting an item's inter-block
    ///      flow across multiple parallel belts / sorters instead of
    ///      aborting when one belt of the chosen tier can't carry it.
    ///
    /// This mod NEVER copies DSPCalculator source. It patches
    /// DSPCalculator's compiled DLL at runtime, reaching every target
    /// via reflection (<see cref="AccessTools"/>), so there is no
    /// compile-time reference to DSPCalculator and no source-license
    /// exposure. If a target member is renamed in a future
    /// DSPCalculator release, the corresponding enhancement is skipped
    /// with a warning and DSPCalculator's stock behaviour is left
    /// untouched.
    ///
    /// All settings live in BepInEx config (see <see cref="PluginConfig"/>).
    /// There is no in-game UI (standing Rule 1).
    ///
    /// Design + verified 0.5.25 Harmony targets:
    ///   cursor-stuff\plans\DSPCalculatorPlus-v1.0.0-initial.md
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("DSPGAME.exe")]
    [BepInDependency(DSPCalculatorGuid)]
    public sealed class Plugin : BaseUnityPlugin
    {
        // ----- mod identity -------------------------------------------------
        public const string PluginGuid = "com.zicarius.DSPCalculatorPlus";
        public const string PluginName = "DSPCalculatorPlus";
        public const string PluginVersion = "1.0.0";

        // DSPCalculator's BepInEx GUID (confirmed against the real 0.5.25
        // DLL - the [BepInPlugin] on DSPCalculatorPlugin). Note this is the
        // *plugin* GUID; the Thunderstore package namespace is "jinxOAO",
        // which is a separate identifier used only in manifest.json.
        public const string DSPCalculatorGuid = "com.GniMaerd.DSPCalculator";

        // ----- shared state -------------------------------------------------
        // 'new' shadows BaseUnityPlugin.Config (the inherited BepInEx
        // ConfigFile); our typed wrapper deliberately reuses the name.
        internal static new PluginConfig Config;
        private static Harmony _harmony;

        // ----- lifecycle ----------------------------------------------------
        private void Awake()
        {
            // 1. Read config. base.Config is the inherited BepInEx ConfigFile;
            //    Config (the field) is our typed wrapper.
            Config = new PluginConfig(base.Config);

            // 2. Wire the log helper BEFORE any patch can log.
            DSPCalculatorPlusLog.Init(Logger, Config.DebugLog);

            DSPCalculatorPlusLog.Info("[config] belt=" + Config.BeltTierOverride.Value
                + ", sorter=" + Config.SorterTierOverride.Value
                + ", overflowFix(externalize)=" + Config.EnableMultiLaneOverflowFix.Value
                + ", pushBeltStacking=" + Config.PushBeltStackingOnOverflow.Value
                + ", autoPowerPoles=" + Config.AutoPowerPoles.Value
                + ", debug=" + Config.DebugLog.Value);

            // 3. Apply Harmony patches. PatchAll auto-discovers every
            //    [HarmonyPatch] class in this assembly. Each patch resolves
            //    its DSPCalculator target by reflection and no-ops (with a
            //    warning) if the target isn't found, so a DSPCalculator
            //    version mismatch degrades gracefully instead of crashing.
            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(Plugin).Assembly);

                // Phase 2 - Group A: belt/sorter tier override. Manual
                // (reflection-targeted) patch, so it resolves its DSPCalculator
                // target at Awake and skips gracefully if absent, rather than
                // throwing from PatchAll. DSPCalculator is a hard dependency,
                // so it is already loaded here.
                TierOverridePatch.Apply(_harmony);

                // Phase 3 - Group B: overflow fix. Over-capacity items are
                // supplied as external logistics inputs (reclassify + re-solve
                // + regenerate) instead of failing generation. Gated by
                // EnableMultiLaneOverflowFix.
                OverflowFixPatch.Apply(_harmony);

                // Group C: auto power poles. Appends Tesla Tower / Satellite
                // Substation buildings covering the generated blueprint's
                // machines, on free tiles only. Targets the game's paste entry
                // point, filtered to DSPCalculator blueprints. Gated by
                // AutoPowerPoles.
                PowerPolePatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                DSPCalculatorPlusLog.Error("Harmony PatchAll failed: " + ex);
            }

            // 4. DIAGNOSTIC (standing Harmony-hygiene rule): dump the real
            //    parameter list of every method we intend to patch, so a
            //    signature drift in a future DSPCalculator release is caught
            //    in one log read rather than a silent no-op. Gated by DebugLog.
            DumpTargetMethodSignatures();
        }

        /// <summary>
        /// Logs the resolved signature of each DSPCalculator method this mod
        /// targets. All targets are private members of DSPCalculator's own
        /// assembly, reached by reflection. If a "Type/Method not found" line
        /// appears, DSPCalculator was updated and the corresponding patch
        /// must be re-verified against the new version. Confirmed present in
        /// DSPCalculator 0.5.25.
        /// </summary>
        private static void DumpTargetMethodSignatures()
        {
            string[][] targets = {
                new[] { "DSPCalculator.Bp.BpConnector", "GenerateFullBlueprint" }, // Group A wrap point
                new[] { "DSPCalculator.Bp.BpConnector", "CalcItemSumInfos" },      // Group B overflow target
            };
            foreach (var t in targets)
            {
                try
                {
                    var type = AccessTools.TypeByName(t[0]);
                    if (type == null) { DSPCalculatorPlusLog.Warn("[diag] Type not found: " + t[0] + " (DSPCalculator not installed, or renamed)"); continue; }
                    var method = AccessTools.Method(type, t[1]);
                    if (method == null) { DSPCalculatorPlusLog.Warn("[diag] Method not found: " + t[0] + "." + t[1]); continue; }
                    var ps = method.GetParameters();
                    var sb = new StringBuilder();
                    sb.Append("[diag] ").Append(t[0]).Append(".").Append(t[1]).Append("(");
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ps[i].ParameterType.Name).Append(" ").Append(ps[i].Name);
                    }
                    sb.Append(") : ").Append(method.ReturnType.Name);
                    DSPCalculatorPlusLog.Info(sb.ToString());
                }
                catch (Exception ex)
                {
                    DSPCalculatorPlusLog.Error("[diag] Failed to inspect " + t[0] + "." + t[1] + ": " + ex.Message);
                }
            }
        }

        // No Start() cross-mod detection retry is needed here: DSPCalculator
        // is a HARD BepInEx dependency ([BepInDependency] above), so BepInEx
        // guarantees it is loaded before this plugin's Awake() runs. (The
        // Start()-retry pattern is only required for SOFT/optional compat
        // layers - see EndlessResources' PlanetMinerFastCompat.)

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}
