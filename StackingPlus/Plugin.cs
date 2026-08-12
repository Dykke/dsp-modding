using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using System.Text;

namespace StackingPlus
{
    /// <summary>
    /// BepInEx entry point for StackingPlus - raises DSP's cargo stacking caps
    /// (sorter output/input, station piler, delivery package) beyond vanilla and
    /// adds an optional belt-speed multiplier. Game-side only; DSPCalculatorPlus
    /// picks up the higher ceiling via its live CalcDB.maxStackSize seam.
    ///
    /// All settings are BepInEx config (Rule 1: no in-game UI). Verbose logging is
    /// gated behind Diagnostics.DebugLog (Rule 2).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.zicarius.StackingPlus";
        public const string PluginName = "StackingPlus";
        public const string PluginVersion = "0.1.0";

        private Harmony harmony;

        // -- Config entries -----------------------------------------------------
        private ConfigEntry<bool> cfgDebugLog;

        private ConfigEntry<bool> cfgEnableOutput;
        private ConfigEntry<int> cfgOutputCap;
        private ConfigEntry<bool> cfgEnableInput;
        private ConfigEntry<int> cfgInputCap;
        private ConfigEntry<bool> cfgEnablePiler;
        private ConfigEntry<int> cfgPilerCap;
        private ConfigEntry<bool> cfgEnablePackage;
        private ConfigEntry<int> cfgPackageCap;
        private ConfigEntry<bool> cfgTechGated;

        private ConfigEntry<int> cfgCeilingOutput;
        private ConfigEntry<int> cfgCeilingInput;
        private ConfigEntry<int> cfgCeilingPiler;
        private ConfigEntry<int> cfgCeilingPackage;

        private ConfigEntry<bool> cfgBeltEnable;
        private ConfigEntry<float> cfgBeltMultiplier;

        private void Awake()
        {
            BindConfig();
            StackingPlusLog.Init(Logger, cfgDebugLog);
            PushConfigToSettings();
            WireLiveConfig();

            StackingPatch.Init();

            harmony = new Harmony(PluginGuid);
            ApplyPatches();

            DumpTargetMethodSignatures();
            LogResolvedConfig();
            StackingPlusLog.Info(PluginName + " " + PluginVersion + " loaded.");
        }

        private void Start()
        {
            // A game may already be loaded (plugin reload); enforce best-effort.
            StackingPatch.EnforceCurrent();
        }

        private void OnDestroy()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
                harmony = null;
            }
            StackingPlusLog.Info(PluginName + " unloaded.");
        }

        // ----------------------------------------------------------------------

        private void BindConfig()
        {
            cfgDebugLog = Config.Bind("Diagnostics", "DebugLog", false,
                "Enable verbose diagnostic logging to the BepInEx console.");

            cfgEnableOutput = Config.Bind("Stacking", "EnableSorterOutput", true,
                "Raise Pile Sorter -> belt output stacking (the belt cargo cap). This is the core throughput fix.");
            cfgOutputCap = Config.Bind("Stacking", "SorterOutputCap", 8,
                new ConfigDescription("Target sorter output stack size (vanilla 4). Cargo.stack is a byte so 255 is the hard ceiling.",
                    new AcceptableValueRange<int>(2, 255)));

            cfgEnableInput = Config.Bind("Stacking", "EnableSorterInput", true,
                "Raise Pile Sorter pickup (input) stacking.");
            cfgInputCap = Config.Bind("Stacking", "SorterInputCap", 8,
                new ConfigDescription("Target sorter input stack size (vanilla 4).",
                    new AcceptableValueRange<int>(2, 255)));

            cfgEnablePiler = Config.Bind("Stacking", "EnableStationPiler", true,
                "Raise the logistics station output piler level.");
            cfgPilerCap = Config.Bind("Stacking", "StationPilerCap", 8,
                new ConfigDescription("Target station piler level (vanilla 4).",
                    new AcceptableValueRange<int>(2, 255)));

            cfgEnablePackage = Config.Bind("Stacking", "EnableDeliveryPackage", true,
                "Raise the delivery package stack-size multiplier.");
            cfgPackageCap = Config.Bind("Stacking", "DeliveryPackageCap", 8,
                new ConfigDescription("Target delivery package multiplier (vanilla 4).",
                    new AcceptableValueRange<int>(2, 255)));

            cfgTechGated = Config.Bind("Stacking", "TechGated", true,
                "If true, each boost applies only once its vanilla ceiling is researched (early game stays vanilla). If false, caps are forced immediately.");

            cfgCeilingOutput = Config.Bind("Advanced", "VanillaCeilingOutput", 4,
                "Vanilla max used for tech-gating sorter output. Override if a DSP update changes it.");
            cfgCeilingInput = Config.Bind("Advanced", "VanillaCeilingInput", 4,
                "Vanilla max used for tech-gating sorter input.");
            cfgCeilingPiler = Config.Bind("Advanced", "VanillaCeilingPiler", 4,
                "Vanilla max used for tech-gating station piler.");
            cfgCeilingPackage = Config.Bind("Advanced", "VanillaCeilingPackage", 4,
                "Vanilla max used for tech-gating delivery package.");

            cfgBeltEnable = Config.Bind("BeltSpeed", "Enable", false,
                "EXPERIMENTAL: multiply belt speed. Default OFF - stacking alone fixes throughput; belt speed needs in-game verification.");
            cfgBeltMultiplier = Config.Bind("BeltSpeed", "Multiplier", 2.0f,
                new ConfigDescription("Belt speed multiplier applied to belts as they are created/rebuilt.",
                    new AcceptableValueRange<float>(1.0f, 10.0f)));
        }

        private void PushConfigToSettings()
        {
            ModSettings.EnableSorterOutput = cfgEnableOutput.Value;
            ModSettings.SorterOutputCap = cfgOutputCap.Value;
            ModSettings.EnableSorterInput = cfgEnableInput.Value;
            ModSettings.SorterInputCap = cfgInputCap.Value;
            ModSettings.EnableStationPiler = cfgEnablePiler.Value;
            ModSettings.StationPilerCap = cfgPilerCap.Value;
            ModSettings.EnableDeliveryPackage = cfgEnablePackage.Value;
            ModSettings.DeliveryPackageCap = cfgPackageCap.Value;
            ModSettings.TechGated = cfgTechGated.Value;

            ModSettings.VanillaCeilingOutput = cfgCeilingOutput.Value;
            ModSettings.VanillaCeilingInput = cfgCeilingInput.Value;
            ModSettings.VanillaCeilingPiler = cfgCeilingPiler.Value;
            ModSettings.VanillaCeilingPackage = cfgCeilingPackage.Value;

            ModSettings.BeltSpeedEnable = cfgBeltEnable.Value;
            ModSettings.BeltSpeedMultiplier = cfgBeltMultiplier.Value;
        }

        private void WireLiveConfig()
        {
            // Re-read into ModSettings and re-enforce whenever any entry in this
            // mod's config file changes (ConfigFile-level event covers all keys).
            Config.SettingChanged += (_, __) =>
            {
                PushConfigToSettings();
                StackingPlusLog.Info("[config] changed; re-enforcing.");
                StackingPatch.EnforceCurrent();
            };
        }

        private void ApplyPatches()
        {
            // Manual, per-target patching so a missing target disables only that
            // patch (defensive - Rule / cross-version strategy) instead of failing
            // the whole PatchAll.
            TryPatch(typeof(GameHistoryData), "UnlockTechFunction", postfix: HM(typeof(StackingPatch), nameof(StackingPatch.AfterUnlockTech)));
            TryPatch(typeof(GameHistoryData), "Import", postfix: HM(typeof(StackingPatch), nameof(StackingPatch.AfterImport)));
            TryPatch(typeof(GameHistoryData), "SetForNewGame", postfix: HM(typeof(StackingPatch), nameof(StackingPatch.AfterSetForNewGame)));

            // Always: post-load hook. Scales belt protos (no-op if belt disabled)
            // and retries any deferred sorter refresh once the game is ready.
            TryPatch(typeof(GameMain), "Begin", postfix: HM(typeof(StackingPatch), nameof(StackingPatch.OnGameBegin)));

            if (ModSettings.BeltSpeedEnable)
            {
                // Rewrite existing belts' path segment speeds on save load.
                TryPatch(typeof(CargoTraffic), "Import", postfix: HM(typeof(BeltSpeedPatch), nameof(BeltSpeedPatch.AfterCargoTrafficImport)));
            }
        }

        private static HarmonyMethod HM(Type type, string method)
        {
            return new HarmonyMethod(type.GetMethod(method,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        }

        private void TryPatch(Type type, string method, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            try
            {
                var mi = AccessTools.Method(type, method);
                if (mi == null)
                {
                    StackingPlusLog.Warn("[compat] target not found: " + type.Name + "." + method + " - patch skipped.");
                    return;
                }
                harmony.Patch(mi, prefix: prefix, postfix: postfix);
                StackingPlusLog.Info("[patch] patched " + type.Name + "." + method);
            }
            catch (Exception ex)
            {
                StackingPlusLog.Error("[patch] failed to patch " + type.Name + "." + method + ": " + ex);
            }
        }

        private void LogResolvedConfig()
        {
            StackingPlusLog.Info("[config] gated=" + ModSettings.TechGated
                + " output=" + ModSettings.EnableSorterOutput + "/" + ModSettings.SorterOutputCap + "(v" + ModSettings.VanillaCeilingOutput + ")"
                + " input=" + ModSettings.EnableSorterInput + "/" + ModSettings.SorterInputCap + "(v" + ModSettings.VanillaCeilingInput + ")"
                + " piler=" + ModSettings.EnableStationPiler + "/" + ModSettings.StationPilerCap + "(v" + ModSettings.VanillaCeilingPiler + ")"
                + " package=" + ModSettings.EnableDeliveryPackage + "/" + ModSettings.DeliveryPackageCap + "(v" + ModSettings.VanillaCeilingPackage + ")"
                + " belt=" + ModSettings.BeltSpeedEnable + "/x" + ModSettings.BeltSpeedMultiplier);
        }

        /// <summary>
        /// Dump the real parameter list of every patched target once at Awake.
        /// Harmony binds by TYPE not name, so a signature drift silently no-ops
        /// the patch; this diagnostic turns that into a one-line fix (workspace rule).
        /// </summary>
        private void DumpTargetMethodSignatures()
        {
            string[][] targets =
            {
                new[] { "GameHistoryData", "UnlockTechFunction" },
                new[] { "GameHistoryData", "Import" },
                new[] { "GameHistoryData", "SetForNewGame" },
                new[] { "GameData", "OnInserterTechChange" },
                new[] { "GameMain", "Begin" },
                new[] { "CargoTraffic", "Import" },
            };
            foreach (var t in targets)
            {
                try
                {
                    var type = AccessTools.TypeByName(t[0]);
                    if (type == null) { StackingPlusLog.Warn("[diag] Type not found: " + t[0]); continue; }
                    var method = AccessTools.Method(type, t[1]);
                    if (method == null) { StackingPlusLog.Warn("[diag] Method not found: " + t[0] + "." + t[1]); continue; }
                    var ps = method.GetParameters();
                    var sb = new StringBuilder();
                    sb.Append("[diag] ").Append(t[0]).Append(".").Append(t[1]).Append("(");
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ps[i].ParameterType.Name).Append(" ").Append(ps[i].Name);
                    }
                    sb.Append(")");
                    StackingPlusLog.Info(sb.ToString());
                }
                catch (Exception ex)
                {
                    StackingPlusLog.Error("[diag] Failed to inspect " + t[0] + "." + t[1] + ": " + ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Single static log helper. Verbose lines gated behind Diagnostics.DebugLog;
    /// warnings/errors always print. All lines tagged [StackingPlus].
    /// </summary>
    internal static class StackingPlusLog
    {
        private static ManualLogSource logger;
        private static bool debugEnabled;

        public static void Init(ManualLogSource src, ConfigEntry<bool> debug)
        {
            logger = src;
            debugEnabled = debug.Value;
            debug.SettingChanged += (_, __) => debugEnabled = debug.Value;
        }

        public static void Info(string msg)
        {
            if (debugEnabled && logger != null) logger.LogInfo("[StackingPlus] " + msg);
        }

        public static void Warn(string msg)
        {
            if (logger != null) logger.LogWarning("[StackingPlus] " + msg);
        }

        public static void Error(string msg)
        {
            if (logger != null) logger.LogError("[StackingPlus] " + msg);
        }
    }
}
