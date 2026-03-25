using UnityEngine;
using Verse;

namespace LearnToSurvive
{
    public class LTSSettings : ModSettings
    {
        // Per-stat toggles
        public static bool enableHaulingSense = true;
        public static bool enableWorkAwareness = true;
        public static bool enablePathMemory = true;
        public static bool enableCombatInstinct = true;
        public static bool enableSelfPreservation = true;

        // XP rate multiplier (1.0 = normal)
        public static float xpMultiplier = 1.0f;

        // Logging
        public static LogLevel logLevel = LogLevel.Off;
        public static bool showLevelUpMessages = true;

        // Compatibility
        public static bool respectPUAH = true;
        public static bool respectCommonSense = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableHaulingSense, "enableHaulingSense", true);
            Scribe_Values.Look(ref enableWorkAwareness, "enableWorkAwareness", true);
            Scribe_Values.Look(ref enablePathMemory, "enablePathMemory", true);
            Scribe_Values.Look(ref enableCombatInstinct, "enableCombatInstinct", true);
            Scribe_Values.Look(ref enableSelfPreservation, "enableSelfPreservation", true);
            Scribe_Values.Look(ref xpMultiplier, "xpMultiplier", 1.0f);
            Scribe_Values.Look(ref logLevel, "logLevel", LogLevel.Off);
            Scribe_Values.Look(ref showLevelUpMessages, "showLevelUpMessages", true);
            Scribe_Values.Look(ref respectPUAH, "respectPUAH", true);
            Scribe_Values.Look(ref respectCommonSense, "respectCommonSense", true);
            base.ExposeData();
        }

        public static bool IsStatEnabled(StatType stat)
        {
            switch (stat)
            {
                case StatType.HaulingSense: return enableHaulingSense;
                case StatType.WorkAwareness: return enableWorkAwareness;
                case StatType.PathMemory: return enablePathMemory;
                case StatType.CombatInstinct: return enableCombatInstinct;
                case StatType.SelfPreservation: return enableSelfPreservation;
                default: return true;
            }
        }

        public static void DrawSettings(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("LTS_Settings_StatToggles".Translate());
            listing.GapLine();
            listing.CheckboxLabeled("LTS_HaulingSense".Translate(), ref enableHaulingSense);
            listing.CheckboxLabeled("LTS_WorkAwareness".Translate(), ref enableWorkAwareness);
            listing.CheckboxLabeled("LTS_PathMemory".Translate(), ref enablePathMemory);
            listing.CheckboxLabeled("LTS_CombatInstinct".Translate(), ref enableCombatInstinct);
            listing.CheckboxLabeled("LTS_SelfPreservation".Translate(), ref enableSelfPreservation);

            listing.GapLine();
            listing.Label("LTS_Settings_XPMultiplier".Translate() + ": " + xpMultiplier.ToString("F1"));
            xpMultiplier = listing.Slider(xpMultiplier, 0.1f, 5.0f);

            listing.GapLine();
            listing.CheckboxLabeled("LTS_Settings_ShowLevelUp".Translate(), ref showLevelUpMessages);

            listing.GapLine();
            listing.Label("LTS_Settings_LogLevel".Translate());
            if (listing.RadioButton("LTS_LogLevel_Off".Translate(), logLevel == LogLevel.Off))
                logLevel = LogLevel.Off;
            if (listing.RadioButton("LTS_LogLevel_Summary".Translate(), logLevel == LogLevel.Summary))
                logLevel = LogLevel.Summary;
            if (listing.RadioButton("LTS_LogLevel_Decisions".Translate(), logLevel == LogLevel.Decisions))
                logLevel = LogLevel.Decisions;
            if (listing.RadioButton("LTS_LogLevel_Verbose".Translate(), logLevel == LogLevel.Verbose))
                logLevel = LogLevel.Verbose;

            if (ModCompat.PUAHLoaded || ModCompat.CommonSenseLoaded)
            {
                listing.GapLine();
                listing.Label("LTS_Settings_Compatibility".Translate());
                if (ModCompat.PUAHLoaded)
                    listing.CheckboxLabeled("LTS_Settings_RespectPUAH".Translate(), ref respectPUAH);
                if (ModCompat.CommonSenseLoaded)
                    listing.CheckboxLabeled("LTS_Settings_RespectCS".Translate(), ref respectCommonSense);
            }

            listing.End();
        }
    }
}
