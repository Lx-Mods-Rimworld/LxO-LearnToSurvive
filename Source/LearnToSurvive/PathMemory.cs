using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Returns additional cost (0 = no change).
        /// NOTE: This method is no longer called from pathfinding patches.
        /// RimWorld 1.6 uses Burst-compiled async pathfinding. ThreadStatic pawn references
        /// don't propagate to Burst worker threads. Path cost modification requires
        /// IPathFindCostProvider, which only supports building-based costs (e.g., traps),
        /// not pawn-specific routing preferences. Kept for potential future use or
        /// non-pathfinding cost evaluation.
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
                if (region != null && comp.IsRegionFamiliar(region.id))
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
        // Cached FieldRef for Pawn_PathFollower.pawn (avoids Traverse overhead every tick)
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> patherPawn =
            AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");

        // Track last known cell per pawn to detect actual cell changes
        private static Dictionary<int, IntVec3> lastCellPerPawn = new Dictionary<int, IntVec3>();
        private static int lastCleanupTick;

        public static void Postfix(Pawn_PathFollower __instance)
        {
            try
            {
                if (!LTSSettings.enablePathMemory) return;
                if (!__instance.Moving) return;

                Pawn pawn = patherPawn(__instance);
                if (pawn == null || !pawn.IsColonist) return;

                // Periodic cleanup of lastCellPerPawn: remove entries for dead/despawned pawns
                int currentTick = Find.TickManager.TicksGame;
                if (currentTick - lastCleanupTick > 10000)
                {
                    lastCleanupTick = currentTick;
                    var deadIds = lastCellPerPawn.Keys
                        .Where(id => !PawnIdIsAlive(id, pawn.Map))
                        .ToList();
                    foreach (int deadId in deadIds)
                        lastCellPerPawn.Remove(deadId);
                }

                // Only increment when pawn actually enters a NEW cell
                int thingId = pawn.thingIDNumber;
                IntVec3 curCell = pawn.Position;
                if (lastCellPerPawn.TryGetValue(thingId, out IntVec3 prevCell) && prevCell == curCell)
                    return; // Same cell, don't count
                lastCellPerPawn[thingId] = curCell;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                comp.tilesWalkedAccumulator++;

                // Record current region
                if (pawn.Map != null && pawn.Position.InBounds(pawn.Map))
                {
                    var region = pawn.Position.GetRegion(pawn.Map);
                    if (region != null)
                        comp.visitedRegions[region.id] = Find.TickManager.TicksGame;
                }
            }
            catch (Exception) { }
        }

        private static bool PawnIdIsAlive(int thingId, Map map)
        {
            if (map == null) return false;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.thingIDNumber == thingId) return true;
            }
            return false;
        }
    }

    // Patch_PathCost REMOVED: RimWorld 1.6 uses Burst-compiled async pathfinding.
    // ThreadStatic pawn references don't propagate to Burst worker threads.
    // Path cost modification requires IPathFindCostProvider which only supports
    // building-based costs (traps), not pawn-specific routing preferences.

    // Patch_FindPath_SetPawn REMOVED: Same Burst threading issue as above.
    // ThreadStatic fields set in prefix are not visible to Burst worker threads.

    // CurrentPathfindingPawn REMOVED: No longer needed without the above patches.

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
