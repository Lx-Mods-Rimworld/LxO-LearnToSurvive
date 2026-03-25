using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    public static class SelfPreservationUtil
    {
        public static bool PreferCookedFood(int level) => level >= 1;
        public static float FoodSearchRadius(int level)
        {
            if (level <= 0) return 0f;
            if (level <= 1) return 25f;
            if (level <= 2) return 40f;
            return 50f;
        }
        public static bool PreferQualityFood(int level) => level >= 3;
        public static bool TableSeeking(int level) => level >= 4;
        public static bool EarlyEating(int level) => level >= 5;
        public static bool EarlySleeping(int level) => level >= 6;
        public static bool RecreationEfficiency(int level) => level >= 7;
        public static bool NeedForecasting(int level) => level >= 8;
        public static bool PromptSelfTend(int level) => level >= 9;
        public static bool MedicineProximity(int level) => level >= 10;
        public static bool FoodSafety(int level) => level >= 11;
        public static bool MedicineMatching(int level) => level >= 12;
        public static bool RecreationVariety(int level) => level >= 13;
        public static bool SocialNeedManagement(int level) => level >= 14;
        public static bool TemperatureAwareness(int level) => level >= 15;
        public static bool MoodManagement(int level) => level >= 16;
        public static bool SubstanceIntelligence(int level) => level >= 17;

        /// <summary>
        /// Calculate food score modifier based on self-preservation level.
        /// Positive = prefer this food, negative = avoid.
        /// </summary>
        public static float GetFoodScoreModifier(Pawn eater, Thing food, int level)
        {
            if (level <= 0) return 0f;
            float modifier = 0f;

            // Prefer cooked food (Lv1-2)
            if (PreferCookedFood(level))
            {
                if (food.def.IsNutritionGivingIngestible)
                {
                    if (food.def.ingestible?.preferability >= FoodPreferability.MealSimple)
                        modifier += 20f;
                    else if (food.def.ingestible != null && food.def.ingestible.preferability < FoodPreferability.MealAwful)
                        modifier -= 10f * level; // Stronger aversion at higher levels
                }
            }

            // Prefer quality food (Lv3+)
            if (PreferQualityFood(level))
            {
                if (food.def.ingestible?.preferability >= FoodPreferability.MealFine)
                    modifier += 30f;
                if (food.def.ingestible?.preferability >= FoodPreferability.MealLavish)
                    modifier += 50f;
            }

            // Food safety (Lv11+): avoid food that will make them sick
            if (FoodSafety(level))
            {
                CompRottable rot = food.TryGetComp<CompRottable>();
                if (rot != null && rot.Stage == RotStage.Dessicated)
                    modifier -= 200f;
                if (rot != null && rot.Stage == RotStage.Rotting)
                    modifier -= 150f;

                // Avoid raw meat
                if (food.def.IsMeat)
                    modifier -= 50f;
            }

            // Table proximity bonus (Lv4+)
            if (TableSeeking(level) && eater.Map != null)
            {
                // Check if there's a table near the food
                bool tableNear = false;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(food.Position, 15f, true))
                {
                    if (!cell.InBounds(eater.Map)) continue;
                    foreach (Thing thing in cell.GetThingList(eater.Map))
                    {
                        if (thing.def.surfaceType == SurfaceType.Eat)
                        {
                            tableNear = true;
                            break;
                        }
                    }
                    if (tableNear) break;
                }
                if (tableNear) modifier += 15f;
            }

            return modifier;
        }

        /// <summary>
        /// For medicine matching (Lv12+): determine appropriate medicine quality for injury severity.
        /// </summary>
        public static ThingDef GetAppropriateMedicine(Pawn patient, int level)
        {
            if (!MedicineMatching(level)) return null;

            // Assess injury severity
            float worstSeverity = 0f;
            bool hasInfection = false;
            bool lifeThreatening = false;

            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
            {
                if (hediff.TendableNow())
                {
                    if (hediff.Severity > worstSeverity)
                        worstSeverity = hediff.Severity;
                    if (hediff.def.lethalSeverity > 0)
                        lifeThreatening = true;
                    if (hediff.def == HediffDefOf.WoundInfection)
                        hasInfection = true;
                }
            }

            // Life-threatening: use best available
            if (lifeThreatening || worstSeverity > 0.7f)
                return null; // null = use best (vanilla behavior)

            // Infections: use industrial medicine
            if (hasInfection || worstSeverity > 0.4f)
                return ThingDefOf.MedicineIndustrial;

            // Minor injuries: use herbal
            return ThingDefOf.MedicineHerbal;
        }
    }

    // ========== HARMONY PATCHES ==========

    /// <summary>
    /// Modify food score to prefer better food based on Self-Preservation level.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
    public static class Patch_FoodOptimality
    {
        public static void Postfix(Pawn eater, Thing foodSource, ThingDef foodDef, float dist,
            ref float __result)
        {
            try
            {
                if (!LTSSettings.enableSelfPreservation) return;
                if (eater == null || foodSource == null) return;

                var comp = eater.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.SelfPreservation);
                if (level <= 0) return;

                float modifier = SelfPreservationUtil.GetFoodScoreModifier(eater, foodSource, level);
                __result += modifier;
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Medicine matching: use appropriate medicine quality for injury severity.
    /// </summary>
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.FindBestMedicine))]
    public static class Patch_FindBestMedicine
    {
        public static void Postfix(Pawn healer, Pawn patient, ref Thing __result)
        {
            try
            {
                if (!LTSSettings.enableSelfPreservation) return;
                if (healer == null || patient == null || __result == null) return;

                // Only apply when self-tending or when healer has high self-preservation
                Pawn checker = (healer == patient) ? patient : healer;
                var comp = checker.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.SelfPreservation);
                if (!SelfPreservationUtil.MedicineMatching(level)) return;

                ThingDef appropriate = SelfPreservationUtil.GetAppropriateMedicine(patient, level);
                if (appropriate == null) return; // Use best = vanilla behavior

                // If current medicine is more expensive than needed, try to find the appropriate one
                if (__result.def.IsNutritionGivingIngestible) return; // Not medicine
                if (__result.def.GetStatValueAbstract(StatDefOf.MedicalPotency) <=
                    appropriate.GetStatValueAbstract(StatDefOf.MedicalPotency))
                    return; // Already using equal or lower quality

                // Find the appropriate medicine nearby
                Thing betterMatch = GenClosest.ClosestThingReachable(
                    patient.Position, patient.Map,
                    ThingRequest.ForDef(appropriate),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(healer),
                    9999f,
                    t => !t.IsForbidden(healer) && healer.CanReserve(t));

                if (betterMatch != null)
                {
                    __result = betterMatch;
                    LTSLog.Decision(checker, StatType.SelfPreservation, level, "MEDICINE_MATCH",
                        "patient=" + patient.LabelShort + " severity=various",
                        "using " + appropriate.label + " instead of better medicine",
                        "saving expensive medicine for serious injuries");
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Early eating: start seeking food before critical hunger (Lv5+).
    /// Modifies the hunger threshold at which pawns seek food.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetFood), nameof(JobGiver_GetFood.GetPriority))]
    public static class Patch_EarlyEating
    {
        public static void Postfix(JobGiver_GetFood __instance, Pawn pawn, ref float __result)
        {
            try
            {
                if (!LTSSettings.enableSelfPreservation) return;
                if (pawn == null || pawn.needs?.food == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.SelfPreservation);
                if (!SelfPreservationUtil.EarlyEating(level)) return;

                // If not yet hungry by vanilla standards but food is getting low, increase priority
                float foodLevel = pawn.needs.food.CurLevelPercentage;
                if (foodLevel < 0.4f && foodLevel > 0.2f)
                {
                    // Boost priority so they eat sooner
                    __result = Math.Max(__result, 6f);

                    LTSLog.Decision(pawn, StatType.SelfPreservation, level, "EARLY_EAT",
                        "food=" + foodLevel.ToString("P0"),
                        "boosting food priority",
                        "EarlyEating level=" + level);
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Early sleeping: start seeking rest before exhaustion (Lv6+).
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetRest), nameof(JobGiver_GetRest.GetPriority))]
    public static class Patch_EarlySleeping
    {
        public static void Postfix(JobGiver_GetRest __instance, Pawn pawn, ref float __result)
        {
            try
            {
                if (!LTSSettings.enableSelfPreservation) return;
                if (pawn == null || pawn.needs?.rest == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.SelfPreservation);
                if (!SelfPreservationUtil.EarlySleeping(level)) return;

                float restLevel = pawn.needs.rest.CurLevelPercentage;
                if (restLevel < 0.35f && restLevel > 0.2f)
                {
                    __result = Math.Max(__result, 6f);

                    LTSLog.Decision(pawn, StatType.SelfPreservation, level, "EARLY_SLEEP",
                        "rest=" + restLevel.ToString("P0"),
                        "boosting rest priority",
                        "EarlySleeping level=" + level);
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Recreation variety: prefer joy types not recently used (Lv13+).
    /// </summary>
    [HarmonyPatch(typeof(JoyUtility), nameof(JoyUtility.JoyTickCheckEnd))]
    public static class Patch_RecreationVariety
    {
        public static void Postfix(Pawn pawn)
        {
            try
            {
                if (!LTSSettings.enableSelfPreservation) return;
                if (pawn == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.SelfPreservation);
                if (SelfPreservationUtil.RecreationEfficiency(level))
                {
                    // Small XP award for recreation
                    comp.AddXP(StatType.SelfPreservation, 0.5f, "recreation");
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Mood management (Lv16+): when mood is trending toward mental break,
    /// boost joy priority to take a break before it's too late.
    /// </summary>
    public class MapComponent_MoodManagement : MapComponent
    {
        public MapComponent_MoodManagement(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!LTSSettings.enableSelfPreservation) return;
            if (Find.TickManager.TicksGame % 500 != 0) return;

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) continue;

                int level = comp.GetLevel(StatType.SelfPreservation);
                if (!SelfPreservationUtil.MoodManagement(level)) continue;

                // Check mood
                if (pawn.needs?.mood == null) continue;
                float mood = pawn.needs.mood.CurLevelPercentage;
                float breakThreshold = pawn.mindState?.mentalBreaker?.BreakThresholdMinor ?? 0.35f;

                // If mood is within 10% of break threshold, suggest taking a joy break
                if (mood < breakThreshold + 0.10f && mood > breakThreshold)
                {
                    // If not already doing recreation, and not doing critical work
                    if (pawn.CurJob != null && pawn.CurJob.def != JobDefOf.Wait
                        && pawn.CurJob.def.joyKind == null)
                    {
                        LTSLog.Decision(pawn, StatType.SelfPreservation, level, "MOOD_MANAGE",
                            "mood=" + mood.ToString("P0") + " threshold=" + breakThreshold.ToString("P0"),
                            "mood management - needs joy",
                            "level=" + level);
                    }
                }

                // Calming presence (Lv20): mood buff to nearby pawns
                if (level >= 20)
                {
                    // This is handled via a thought that checks for nearby Lv20 pawns
                    // See ThoughtWorker_CalmingPresence
                }
            }
        }
    }
}
