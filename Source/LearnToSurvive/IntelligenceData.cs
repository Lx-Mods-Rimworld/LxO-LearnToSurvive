using System;
using System.Collections.Generic;
using Verse;

namespace LearnToSurvive
{
    public enum StatType : byte
    {
        HaulingSense = 0,
        WorkAwareness = 1,
        PathMemory = 2,
        CombatInstinct = 3,
        SelfPreservation = 4
    }

    public class IntelligenceData : IExposable
    {
        public const int MaxLevel = 20;

        public StatType statType;
        public int level;
        public float xp;

        // Tier unlock levels for each major behavior change
        public static readonly int[] TierLevels = { 4, 8, 12, 16, 20 };

        public IntelligenceData() { }

        public IntelligenceData(StatType type, int startLevel = 0)
        {
            statType = type;
            level = Math.Min(startLevel, MaxLevel);
            xp = 0f;
        }

        public float XPForNextLevel
        {
            get
            {
                if (level >= MaxLevel) return 0f;
                return XPRequiredForLevel(level + 1);
            }
        }

        public float XPProgress => XPForNextLevel > 0 ? xp / XPForNextLevel : 1f;

        public static float XPRequiredForLevel(int targetLevel)
        {
            if (targetLevel <= 0) return 0f;
            return (float)Math.Round(250.0 * Math.Pow(1.28, targetLevel - 1));
        }

        public static float TotalXPForLevel(int targetLevel)
        {
            float total = 0f;
            for (int i = 1; i <= targetLevel; i++)
                total += XPRequiredForLevel(i);
            return total;
        }

        public bool TryAddXP(float amount)
        {
            if (level >= MaxLevel) return false;
            xp += amount;
            bool leveledUp = false;
            while (level < MaxLevel && xp >= XPForNextLevel)
            {
                xp -= XPForNextLevel;
                level++;
                leveledUp = true;
            }
            if (level >= MaxLevel) xp = 0f;
            return leveledUp;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref statType, "statType");
            Scribe_Values.Look(ref level, "level", 0);
            Scribe_Values.Look(ref xp, "xp", 0f);
        }

        public static string GetStatLabel(StatType type)
        {
            switch (type)
            {
                case StatType.HaulingSense: return "LTS_HaulingSense".Translate();
                case StatType.WorkAwareness: return "LTS_WorkAwareness".Translate();
                case StatType.PathMemory: return "LTS_PathMemory".Translate();
                case StatType.CombatInstinct: return "LTS_CombatInstinct".Translate();
                case StatType.SelfPreservation: return "LTS_SelfPreservation".Translate();
                default: return type.ToString();
            }
        }

        public static string GetTierName(StatType type, int level)
        {
            switch (type)
            {
                case StatType.HaulingSense: return GetHaulingTierName(level);
                case StatType.WorkAwareness: return GetWorkTierName(level);
                case StatType.PathMemory: return GetPathTierName(level);
                case StatType.CombatInstinct: return GetCombatTierName(level);
                case StatType.SelfPreservation: return GetSelfPresTierName(level);
                default: return "Unknown";
            }
        }

        private static string GetHaulingTierName(int lv)
        {
            if (lv <= 0) return "LTS_Tier_Untrained".Translate();
            if (lv <= 3) return "LTS_Tier_StackCollector".Translate();
            if (lv <= 4) return "LTS_Tier_InventoryHauler".Translate();
            if (lv <= 7) return "LTS_Tier_ProximityHauler".Translate();
            if (lv <= 8) return "LTS_Tier_RoutePlanner".Translate();
            if (lv <= 11) return "LTS_Tier_EfficientHauler".Translate();
            if (lv <= 12) return "LTS_Tier_Opportunist".Translate();
            if (lv <= 15) return "LTS_Tier_LogisticsExpert".Translate();
            if (lv <= 16) return "LTS_Tier_TeamCoordinator".Translate();
            if (lv <= 19) return "LTS_Tier_SupplyMaster".Translate();
            return "LTS_Tier_HaulingMentor".Translate();
        }

        private static string GetWorkTierName(int lv)
        {
            if (lv <= 0) return "LTS_Tier_Untrained".Translate();
            if (lv <= 3) return "LTS_Tier_LocalWorker".Translate();
            if (lv <= 5) return "LTS_Tier_TidyWorker".Translate();
            if (lv <= 7) return "LTS_Tier_PreparedWorker".Translate();
            if (lv <= 8) return "LTS_Tier_TaskChainer".Translate();
            if (lv <= 11) return "LTS_Tier_SmartWorker".Translate();
            if (lv <= 12) return "LTS_Tier_StationExpert".Translate();
            if (lv <= 15) return "LTS_Tier_EfficiencyExpert".Translate();
            if (lv <= 16) return "LTS_Tier_WorkflowMaster".Translate();
            if (lv <= 19) return "LTS_Tier_ColonyBackbone".Translate();
            return "LTS_Tier_WorkMentor".Translate();
        }

        private static string GetPathTierName(int lv)
        {
            if (lv <= 0) return "LTS_Tier_Untrained".Translate();
            if (lv <= 3) return "LTS_Tier_PathLearner".Translate();
            if (lv <= 4) return "LTS_Tier_ShortcutFinder".Translate();
            if (lv <= 7) return "LTS_Tier_RoomRespecter".Translate();
            if (lv <= 8) return "LTS_Tier_DangerMapper".Translate();
            if (lv <= 11) return "LTS_Tier_TerrainReader".Translate();
            if (lv <= 12) return "LTS_Tier_WeatherRouter".Translate();
            if (lv <= 15) return "LTS_Tier_SocialNavigator".Translate();
            if (lv <= 16) return "LTS_Tier_KnowledgeSharer".Translate();
            if (lv <= 19) return "LTS_Tier_RouteMaster".Translate();
            return "LTS_Tier_NavigationMentor".Translate();
        }

        private static string GetCombatTierName(int lv)
        {
            if (lv <= 0) return "LTS_Tier_Untrained".Translate();
            if (lv <= 2) return "LTS_Tier_HazardAware".Translate();
            if (lv <= 4) return "LTS_Tier_CoverSeeker".Translate();
            if (lv <= 7) return "LTS_Tier_FireDisciplined".Translate();
            if (lv <= 8) return "LTS_Tier_TargetPrioritizer".Translate();
            if (lv <= 11) return "LTS_Tier_TacticallyAware".Translate();
            if (lv <= 12) return "LTS_Tier_CombatRetreater".Translate();
            if (lv <= 15) return "LTS_Tier_SquadFighter".Translate();
            if (lv <= 16) return "LTS_Tier_Suppressor".Translate();
            if (lv <= 19) return "LTS_Tier_BattlefieldVet".Translate();
            return "LTS_Tier_CombatCommander".Translate();
        }

        private static string GetSelfPresTierName(int lv)
        {
            if (lv <= 0) return "LTS_Tier_Untrained".Translate();
            if (lv <= 3) return "LTS_Tier_FoodAware".Translate();
            if (lv <= 4) return "LTS_Tier_TableSeeker".Translate();
            if (lv <= 7) return "LTS_Tier_NeedAnticipator".Translate();
            if (lv <= 8) return "LTS_Tier_NeedForecaster".Translate();
            if (lv <= 11) return "LTS_Tier_HealthAware".Translate();
            if (lv <= 12) return "LTS_Tier_MedicineMatcher".Translate();
            if (lv <= 15) return "LTS_Tier_ComfortOptimizer".Translate();
            if (lv <= 16) return "LTS_Tier_MoodManager".Translate();
            if (lv <= 19) return "LTS_Tier_SurvivalExpert".Translate();
            return "LTS_Tier_CalmingPresence".Translate();
        }

        public string GetCurrentAbilityDescription()
        {
            string key = "LTS_Ability_" + statType.ToString() + "_" + level;
            if (key.CanTranslate())
                return key.Translate();
            return "";
        }

        public string GetNextAbilityDescription()
        {
            if (level >= MaxLevel) return "";
            string key = "LTS_Ability_" + statType.ToString() + "_" + (level + 1);
            if (key.CanTranslate())
                return key.Translate();
            return "";
        }
    }
}
