namespace StackingPlus
{
    /// <summary>
    /// Resolved, plain-value snapshot of the BepInEx config, populated once in
    /// Plugin.Awake and re-read live where the ConfigEntry SettingChanged fires.
    /// The patch classes read from here so they never touch BepInEx types.
    ///
    /// All settings originate from BepInEx Config.Bind (see Plugin.Awake). There
    /// is deliberately NO in-game settings UI (workspace Rule 1).
    /// </summary>
    internal static class ModSettings
    {
        // -- Stacking dimensions ------------------------------------------------
        public static bool EnableSorterOutput = true;
        public static int SorterOutputCap = 8;
        public static bool EnableSorterInput = true;
        public static int SorterInputCap = 8;
        public static bool EnableStationPiler = true;
        public static int StationPilerCap = 8;
        public static bool EnableDeliveryPackage = true;
        public static int DeliveryPackageCap = 8;

        /// <summary>
        /// When true, a dimension's boost applies only once its vanilla ceiling
        /// has been researched; when false the cap is forced immediately.
        /// </summary>
        public static bool TechGated = true;

        // -- Vanilla ceilings (tech-gating reference; overridable in .cfg) ------
        public static int VanillaCeilingOutput = 4;
        public static int VanillaCeilingInput = 4;
        public static int VanillaCeilingPiler = 4;
        public static int VanillaCeilingPackage = 4;

        // -- Belt speed (optional, experimental, default OFF) -------------------
        public static bool BeltSpeedEnable = false;
        public static float BeltSpeedMultiplier = 2.0f;

        /// <summary>Cargo.stack is a byte, so this is the true hard ceiling.</summary>
        public const int HardStackCeiling = 255;

        public static int ClampStack(int v)
        {
            if (v < 2) return 2;
            if (v > HardStackCeiling) return HardStackCeiling;
            return v;
        }
    }
}
