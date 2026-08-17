using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Text;

namespace PlanetwideAmmoSupply
{
    /// <summary>
    /// BepInEx plugin entry point for PlanetwideAmmoSupply.
    ///
    /// Auto-restocks combat structures (turrets, battle bases) with
    /// ammo/fighters pulled from the planet's logistics network
    /// (ILS/PLS), consuming real stock. Config-only, no in-game UI.
    ///
    /// Lifecycle:
    ///   Awake      - read Config, init Harmony, apply patches, dump target signatures
    ///   Start      - post-init, after all plugins loaded
    ///   OnDestroy  - cleanup, Harmony.UnpatchSelf
    ///
    /// The refill patches themselves (DefenseSystem.GameTick_Turret and
    /// BattleBaseComponent.InternalUpdate postfixes) land in Patches\ in
    /// Phase 2/3 - see the plan. Phase 0 = confirm the target signatures
    /// via the DumpTargetMethodSignatures diagnostic below (Harmony
    /// binds by TYPE, not name; a mismatch silently no-ops).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        // -- Plugin identity -------------------------------------------------
        public const string PluginGuid = "com.zicarius.PlanetwideAmmoSupply";
        public const string PluginName = "PlanetwideAmmoSupply";
        public const string PluginVersion = "1.0.0";

        // -- Config (BepInEx only - Rule 1: never an in-game settings UI) ----
        // Exposed static so the Phase 2/3 patch classes can read them live.
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SupplyTurrets;
        internal static ConfigEntry<bool> SupplyBattleBases;
        internal static ConfigEntry<bool> PreferHighestAmmoTier;
        internal static ConfigEntry<float> SupplyRadius;
        internal static ConfigEntry<bool> NearestStationFirst;
        internal static ConfigEntry<int> RefillIntervalTicks;
        internal static ConfigEntry<bool> RequireStationSupplyFlag;
        internal static ConfigEntry<string> FighterItemFilter;
        internal static ConfigEntry<bool> VerboseScan;
        internal static ConfigEntry<bool> DebugLog;

        // -- Harmony ---------------------------------------------------------
        private Harmony harmony;

        private void Awake()
        {
            // Diagnostics gate (Rule 2), off by default.
            DebugLog = Config.Bind(
                "Diagnostics", "DebugLog", false,
                "Enable verbose diagnostic logging to the BepInEx console.");

            // General.
            Enabled = Config.Bind(
                "General", "Enabled", true,
                "Master switch. If false the mod is fully inert (vanilla belt supply unchanged).");
            SupplyTurrets = Config.Bind(
                "General", "SupplyTurrets", true,
                "Auto-refill turret ammo from the planet's logistics stations.");
            SupplyBattleBases = Config.Bind(
                "General", "SupplyBattleBases", true,
                "Auto-refill battle-base ammo and fighters from the planet's logistics stations.");
            PreferHighestAmmoTier = Config.Bind(
                "General", "PreferHighestAmmoTier", true,
                "When auto-filling an EMPTY turret, pick the highest available ammo tier (true) or the lowest/cheapest (false). A turret that already holds an ammo item keeps that item's tier - this only chooses the first fill.");
            SupplyRadius = Config.Bind(
                "General", "SupplyRadius", 0f,
                "Max straight-line distance (metres) from a structure to an eligible station. 0 = planetwide (recommended - matches how PLS/ILS serve a whole planet). Scale: a standard planet is ~200m radius, so distances reach ~400m (opposite side). Rough guide: ~50 = tight cluster, ~150 = large base, ~300 = most of the planet, ~400 = whole standard planet. Small values like 10 are basically 'touching' and will match nothing.");
            NearestStationFirst = Config.Bind(
                "General", "NearestStationFirst", true,
                "Pull from the closest eligible station first (true) instead of station build order (false). Pairs naturally with SupplyRadius.");
            RefillIntervalTicks = Config.Bind(
                "General", "RefillIntervalTicks", 60,
                "Game ticks between refill scans (throttle). ~60 = 1s. Higher = cheaper, lower = more responsive.");

            // Advanced.
            RequireStationSupplyFlag = Config.Bind(
                "Advanced", "RequireStationSupplyFlag", false,
                "If true, only pull from station slots set to 'Supply' (never 'Demand'/'Storage'). False = pull from any slot holding the ammo.");
            FighterItemFilter = Config.Bind(
                "Advanced", "FighterItemFilter", "",
                "Optional: restrict which fighter/ammo items battle bases pull (empty = everything the base already accepts).");
            VerboseScan = Config.Bind(
                "Advanced", "VerboseScan", false,
                "Debug aid (needs DebugLog on): log a periodic scan heartbeat on the active planet (~1 per 5s) showing total/belowCap/refilled/noStock even when nothing moved - so you can see it working and why. Off = only log when ammo actually moves.");

            // Logging helper.
            PlanetwideAmmoSupplyLog.Init(Logger, DebugLog);
            PlanetwideAmmoSupplyLog.Info("[config] Enabled=" + Enabled.Value
                + " SupplyTurrets=" + SupplyTurrets.Value
                + " SupplyBattleBases=" + SupplyBattleBases.Value
                + " SupplyRadius=" + SupplyRadius.Value
                + " RefillIntervalTicks=" + RefillIntervalTicks.Value
                + " RequireStationSupplyFlag=" + RequireStationSupplyFlag.Value
                + " PreferHighestAmmoTier=" + PreferHighestAmmoTier.Value
                + " NearestStationFirst=" + NearestStationFirst.Value
                + " VerboseScan=" + VerboseScan.Value);

            // Harmony. PatchAll is a no-op until the Phase 2/3 patch classes
            // are added under Patches\ - the mod loads and does nothing yet.
            harmony = new Harmony(PluginGuid);
            try
            {
                harmony.PatchAll();
                PlanetwideAmmoSupplyLog.Info("[patch] PatchAll complete.");
            }
            catch (Exception ex)
            {
                PlanetwideAmmoSupplyLog.Error("[patch] PatchAll failed: " + ex);
            }

            // Phase 0: dump the real signatures of the intended targets so we
            // can confirm them against the live DLL before writing patch bodies.
            if (DebugLog.Value)
            {
                DumpTargetMethodSignatures();
            }

            PlanetwideAmmoSupplyLog.Info(PluginName + " " + PluginVersion + " loaded.");
        }

        private void Start()
        {
            // Post-init. No cross-mod detection needed (vanilla combat targets),
            // so no Start()-retry is required here.
        }

        private void OnDestroy()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
                harmony = null;
            }
            PlanetwideAmmoSupplyLog.Info(PluginName + " unloaded.");
        }

        /// <summary>
        /// Phase 0 diagnostic. Prints the real parameter list of every
        /// intended Harmony target and the presence of the key
        /// pool/field members, using string-based reflection so it compiles
        /// and runs regardless of whether the members were renamed. Gated
        /// behind DebugLog. Standing Harmony-hygiene rule for every mod.
        /// </summary>
        private static void DumpTargetMethodSignatures()
        {
            // Methods we intend to patch: {typeName, methodName}.
            string[][] methods =
            {
                new[] { "DefenseSystem", "GameTick" },
                new[] { "StorageComponent", "AddItem" },
            };
            foreach (var t in methods)
            {
                try
                {
                    var type = AccessTools.TypeByName(t[0]);
                    if (type == null) { PlanetwideAmmoSupplyLog.Warn("[diag] Type not found: " + t[0]); continue; }
                    // StorageComponent.AddItem has 6 overloads sharing that name - an
                    // unqualified AccessTools.Method(type, name) lookup throws
                    // "Ambiguous match" for it. This is the one this mod actually calls
                    // (see DefenseSystemSupplyPatch.cs): AddItem(int, int, int, out int, bool).
                    var method = t[0] == "StorageComponent" && t[1] == "AddItem"
                        ? AccessTools.Method(type, t[1], new[] { typeof(int), typeof(int), typeof(int), typeof(int).MakeByRefType(), typeof(bool) })
                        : AccessTools.Method(type, t[1]);
                    if (method == null) { PlanetwideAmmoSupplyLog.Warn("[diag] Method not found: " + t[0] + "." + t[1]); continue; }
                    var ps = method.GetParameters();
                    var sb = new StringBuilder();
                    sb.Append("[diag] ").Append(t[0]).Append('.').Append(t[1]).Append('(');
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ps[i].ParameterType.Name).Append(' ').Append(ps[i].Name);
                    }
                    sb.Append(')');
                    PlanetwideAmmoSupplyLog.Info(sb.ToString());
                }
                catch (Exception ex)
                {
                    PlanetwideAmmoSupplyLog.Error("[diag] Failed to inspect " + t[0] + "." + t[1] + ": " + ex.Message);
                }
            }

            // Fields/pools we intend to read: {typeName, fieldName}.
            string[][] fields =
            {
                new[] { "DefenseSystem", "turrets" },
                new[] { "DefenseSystem", "battleBases" },
                new[] { "BattleBaseComponent", "storage" },
                new[] { "PlanetFactory", "transport" },
                new[] { "PlanetTransport", "stationPool" },
                new[] { "StationComponent", "storage" },
            };
            foreach (var t in fields)
            {
                try
                {
                    var type = AccessTools.TypeByName(t[0]);
                    if (type == null) { PlanetwideAmmoSupplyLog.Warn("[diag] Type not found: " + t[0]); continue; }
                    var field = AccessTools.Field(type, t[1]);
                    if (field == null) { PlanetwideAmmoSupplyLog.Warn("[diag] Field not found: " + t[0] + "." + t[1]); continue; }
                    PlanetwideAmmoSupplyLog.Info("[diag] field " + t[0] + "." + t[1] + " : " + field.FieldType.Name);
                }
                catch (Exception ex)
                {
                    PlanetwideAmmoSupplyLog.Error("[diag] Failed to inspect field " + t[0] + "." + t[1] + ": " + ex.Message);
                }
            }
        }
    }
}
