using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    public static class PathMemoryUtil
    {
        public static bool AvoidBedrooms(int level) => level >= 5;
        public static bool AvoidHospitals(int level) => level >= 6;
        public static bool AvoidSensitiveRooms(int level) => level >= 7;
        public static bool DangerMemory(int level) => level >= 8;
        public static bool PreferFloors(int level) => level >= 9;
        public static bool AvoidMud(int level) => level >= 10;
        public static bool EfficientDoors(int level) => level >= 11;
        public static bool WeatherRouting(int level) => level >= 12;
        public static bool SharedKnowledge(int level) => level >= 16;

        /// <summary>
        /// Calculate extra path cost for a cell based on pawn's Path Memory level.
        /// Returns additional cost (0 = no change). Used by pathfinding cost patch.
        /// </summary>
        public static int GetExtraPathCost(Pawn pawn, IntVec3 cell, int level, CompIntelligence comp)
        {
            if (level <= 0 || pawn.Map == null) return 0;
            int extra = 0;

            // Room avoidance (Lv5-7)
            if (AvoidBedrooms(level))
            {
                Room room = cell.GetRoom(pawn.Map);
                if (room != null && !room.PsychologicallyOutdoors)
                {
                    var role = room.Role;
                    if (role != null)
                    {
                        // Avoid bedrooms that aren't ours
                        if (role == RoomRoleDefOf.Bedroom || role == RoomRoleDefOf.Barracks)
                        {
                            bool isOurRoom = false;
                            foreach (var bed in room.ContainedBeds)
                            {
                                if (bed.OwnersForReading != null && bed.OwnersForReading.Contains(pawn))
                                {
                                    isOurRoom = true;
                                    break;
                                }
                            }
                            if (!isOurRoom) extra += 15;
                        }

                        // Avoid hospitals (Lv6+)
                        if (AvoidHospitals(level) && role == RoomRoleDefOf.Hospital)
                            extra += 20;

                        // Avoid prisons (Lv6+)
                        if (AvoidHospitals(level) && role == RoomRoleDefOf.PrisonCell)
                            extra += 20;

                        // Avoid sensitive rooms like throne rooms, labs (Lv7+)
                        if (AvoidSensitiveRooms(level))
                        {
                            if (role == RoomRoleDefOf.ThroneRoom || role == RoomRoleDefOf.Laboratory)
                                extra += 25;
                        }
                    }
                }
            }

            // Danger memory (Lv8+)
            if (DangerMemory(level) && comp != null && comp.IsDangerCell(cell))
            {
                extra += 50;
            }

            // Floor preference (Lv9+): prefer constructed floors
            if (PreferFloors(level))
            {
                TerrainDef terrain = cell.GetTerrain(pawn.Map);
                if (terrain != null && !terrain.layerable && terrain.passability == Traversability.Standable)
                {
                    // Natural/rough terrain gets a small penalty
                    if (terrain.defName.Contains("Rough") || terrain.defName.Contains("Soil")
                        || terrain.defName.Contains("Gravel") || terrain.defName.Contains("Sand"))
                    {
                        extra += 3;
                    }
                }
            }

            // Mud/water avoidance (Lv10+)
            if (AvoidMud(level))
            {
                TerrainDef terrain = cell.GetTerrain(pawn.Map);
                if (terrain != null)
                {
                    if (terrain.defName.Contains("Mud") || terrain.defName.Contains("Marsh"))
                        extra += 10;
                    if (terrain == TerrainDefOf.WaterShallow)
                        extra += 8;
                }
            }

            // Weather routing (Lv12+): prefer roofed areas during bad weather
            if (WeatherRouting(level) && pawn.Map.weatherManager != null)
            {
                bool badWeather = pawn.Map.weatherManager.curWeather.rainRate > 0.1f
                    || pawn.Map.weatherManager.curWeather.snowRate > 0.1f
                    || pawn.Map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout);

                if (badWeather && !cell.Roofed(pawn.Map))
                {
                    extra += 8;
                }
            }

            // Familiarity bonus (Lv1-4): reduce cost for visited regions
            if (level >= 1 && comp != null)
            {
                Region region = cell.GetRegion(pawn.Map);
                if (region != null && comp.visitedRegions.Contains(region.id))
                {
                    int familiarityBonus = Math.Min(level, 4); // 1-4 reduction
                    extra -= familiarityBonus;
                }
            }

            return Math.Max(extra, -5); // Don't reduce cost too much
        }
    }

    // ========== HARMONY PATCHES ==========

    /// <summary>
    /// Track tiles walked for passive Path Memory XP.
    /// </summary>
    // Manually patched in HarmonyInit - Pawn_PathFollower.PatherTick
    public static class Patch_PathFollower_Track
    {
        public static void Postfix(Pawn_PathFollower __instance)
        {
            try
            {
                if (!LTSSettings.enablePathMemory) return;

                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawn == null || !pawn.IsColonist) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                comp.tilesWalkedAccumulator++;

                // Record current region
                if (pawn.Map != null && pawn.Position.InBounds(pawn.Map))
                {
                    var region = pawn.Position.GetRegion(pawn.Map);
                    if (region != null)
                        comp.visitedRegions.Add(region.id);
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Modify pathfinding costs based on Path Memory.
    /// Patches the path grid cost calculation.
    /// </summary>
    // Manually patched in HarmonyInit - PathGrid.CalculatedCostAt
    public static class Patch_PathCost
    {
        public static void Postfix(PathGrid __instance, IntVec3 c, bool perceivedStatic,
            IntVec3 prevCell, ref int __result)
        {
            try
            {
                if (!LTSSettings.enablePathMemory) return;

                // We need to find the pawn currently pathfinding
                // PathGrid doesn't have a pawn reference, so we use a threadlocal
                Pawn pawn = CurrentPathfindingPawn.Value;
                if (pawn == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.PathMemory);
                if (level <= 0) return;

                int extra = PathMemoryUtil.GetExtraPathCost(pawn, c, level, comp);
                if (extra != 0)
                {
                    __result = Math.Max(1, __result + extra);
                }
            }
            catch (Exception) { }
        }

        // ThreadLocal to track which pawn is currently pathfinding
        [ThreadStatic]
        public static Pawn Value;
    }

    /// <summary>
    /// Set the current pathfinding pawn before FindPath runs.
    /// </summary>
    // Manually patched in HarmonyInit - PathFinder.FindPath
    public static class Patch_FindPath_SetPawn
    {
        public static void Prefix(TraverseParms traverseParms)
        {
            try
            {
                CurrentPathfindingPawn.Value = traverseParms.pawn;
            }
            catch (Exception) { }
        }

        public static void Postfix()
        {
            CurrentPathfindingPawn.Value = null;
        }

        // Shared with Patch_PathCost - stores pawn during pathfinding
        [ThreadStatic]
        public static Pawn Value;
    }

    /// <summary>
    /// Bridge between FindPath and PathCost patches.
    /// </summary>
    public static class CurrentPathfindingPawn
    {
        [ThreadStatic]
        public static Pawn Value;
    }

    /// <summary>
    /// Record danger when a pawn takes damage.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_RecordDanger
    {
        public static void Postfix(Pawn __instance, ref DamageInfo dinfo)
        {
            try
            {
                if (!LTSSettings.enablePathMemory) return;
                if (__instance == null || !__instance.IsColonist) return;

                var comp = __instance.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.PathMemory);
                if (!PathMemoryUtil.DangerMemory(level)) return;

                comp.RecordDanger(__instance.Position);

                LTSLog.Decision(__instance, StatType.PathMemory, level, "DANGER_RECORD",
                    "damage at " + __instance.Position,
                    "recorded danger zone",
                    "DangerMemory level=" + level);
            }
            catch (Exception) { }
        }
    }
}
