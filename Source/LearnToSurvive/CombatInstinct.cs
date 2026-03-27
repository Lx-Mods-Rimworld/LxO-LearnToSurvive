using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    public static class CombatInstinctUtil
    {
        public static bool FastHazardResponse(int level) => level >= 1;
        public static bool BasicCover(int level) => level >= 3;
        public static bool CoverEvaluation(int level) => level >= 4;
        public static bool BasicFFCheck(int level) => level >= 5;
        // FFConeAngle removed: friendly fire now uses vanilla-accurate ShootLine intercept check
        // instead of cone-based approximation. BasicFFCheck(level >= 5) is the only gate.
        public static bool SmartTargeting(int level) => level >= 8;
        public static bool WoundedFallback(int level) => level >= 9;
        public static bool CombatReposition(int level) => level >= 10;
        public static bool ThreatAssessment(int level) => level >= 11;
        public static bool AutoRetreat(int level) => level >= 12;
        public static bool AoESpacing(int level) => level >= 13;
        public static bool RolePositioning(int level) => level >= 14;
        public static bool FocusFire(int level) => level >= 15;
        public static bool Suppression(int level) => level >= 16;
        public static bool Flanking(int level) => level >= 17;
        public static bool WeaponRange(int level) => level >= 18;
        public static float RetreatHealthPct(int level) => level >= 12 ? 0.25f : (level >= 9 ? 0.40f : 0f);

        /// <summary>
        /// Find the best cover cell near the pawn's current position.
        /// </summary>
        public static IntVec3 FindBestCover(Pawn pawn, int level)
        {
            if (pawn.Map == null) return IntVec3.Invalid;

            float searchRadius = level >= 4 ? 5f : 3f;
            IntVec3 bestCell = IntVec3.Invalid;
            float bestCover = -1f;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, searchRadius, true))
            {
                if (!cell.InBounds(pawn.Map)) continue;
                if (!cell.Standable(pawn.Map)) continue;
                if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;

                float coverScore = CoverUtility.TotalSurroundingCoverScore(cell, pawn.Map);
                if (coverScore > bestCover)
                {
                    bestCover = coverScore;
                    bestCell = cell;
                }
            }

            return bestCell;
        }

        /// <summary>
        /// Check if firing at a target would risk hitting a friendly pawn.
        /// Uses vanilla-accurate intercept check based on ShootLine cells.
        /// </summary>
        public static bool WouldHitFriendly(Pawn shooter, LocalTargetInfo target, Verb verb)
        {
            if (!target.HasThing || shooter.Map == null || verb == null) return false;

            // Overhead projectiles (mortars) skip en-route interception entirely
            if (verb.verbProps != null && !verb.verbProps.requireLineOfSight) return false;

            // Get the actual shoot line the verb would use
            if (!verb.TryFindShootLineFromTo(shooter.Position, target, out ShootLine shootLine, false))
                return false;

            // Walk each cell on the shoot line, check for friendly pawns
            float totalRisk = 0f;
            foreach (IntVec3 cell in shootLine.Points())
            {
                // Skip the shooter's own cell and the target cell
                if (cell == shooter.Position) continue;
                if (cell == target.Cell) continue;

                List<Thing> things = cell.GetThingList(shooter.Map);
                for (int i = 0; i < things.Count; i++)
                {
                    Pawn ally = things[i] as Pawn;
                    if (ally == null || ally == shooter) continue;
                    if (ally.Dead) continue;
                    if (ally.Faction == null || !ally.Faction.IsPlayer) continue;

                    // Vanilla intercept chance: 0.4f * BodySize * (downed ? 0.1f : 1f)
                    float interceptChance = 0.4f * ally.BodySize * (ally.Downed ? 0.1f : 1f);
                    totalRisk += interceptChance;
                }
            }

            // 5% total risk threshold
            return totalRisk > 0.05f;
        }

    }

    // ========== HARMONY PATCHES ==========

    /// <summary>
    /// Award combat XP while drafted and in combat.
    /// </summary>
    // Manually patched in HarmonyInit - Pawn.Tick
    public static class Patch_PawnTick_CombatXP
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (!LTSSettings.enableCombatInstinct) return;
                if (__instance == null || __instance.Dead || !__instance.IsColonist) return;
                if (!__instance.Drafted) return;

                // Only every 60 ticks
                if (Find.TickManager.TicksGame % 60 != 0) return;

                // Check if actually in combat (enemies nearby)
                if (__instance.Map == null) return;
                bool inCombat = __instance.Map.attackTargetsCache.GetPotentialTargetsFor(__instance).Any();
                if (!inCombat) return;

                var comp = __instance.GetComp<CompIntelligence>();
                comp?.AddXP(StatType.CombatInstinct, 2f, "combat_tick");
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Friendly fire prevention: hold fire if ally is in the way.
    /// Uses low priority so other mods' patches run first.
    /// Compatibility note: this prefix can still block the shot (return false),
    /// but low priority means other mods get first say.
    /// </summary>
    // Manually patched in HarmonyInit - Verb_LaunchProjectile.TryCastShot
    // NO [HarmonyPatch] attribute -- this is registered manually with Priority.Low
    public static class Patch_FriendlyFire
    {
        public static bool Prefix(Verb_LaunchProjectile __instance, ref bool __result)
        {
            try
            {
                if (!LTSSettings.enableCombatInstinct) return true;

                Pawn shooter = __instance.CasterPawn;
                if (shooter == null || !shooter.IsColonist) return true;

                var comp = shooter.GetComp<CompIntelligence>();
                if (comp == null) return true;

                int level = comp.GetLevel(StatType.CombatInstinct);
                if (!CombatInstinctUtil.BasicFFCheck(level)) return true;

                LocalTargetInfo target = __instance.CurrentTarget;

                if (CombatInstinctUtil.WouldHitFriendly(shooter, target, __instance))
                {
                    LTSLog.Decision(shooter, StatType.CombatInstinct, level, "FF_HOLD",
                        "target=" + target.Thing?.LabelShort,
                        "holding fire - ally in shoot line",
                        "level=" + level);

                    __result = false;
                    return false; // Skip original - don't fire
                }

                return true;
            }
            catch (Exception)
            {
                return true; // On error, allow vanilla behavior
            }
        }
    }

    /// <summary>
    /// Award bonus XP when downing/killing an enemy.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_EnemyKill_XP
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                if (!LTSSettings.enableCombatInstinct) return;
                if (__instance == null || dinfo == null) return;

                Pawn killer = dinfo.Value.Instigator as Pawn;
                if (killer == null || !killer.IsColonist) return;
                if (killer.Faction == __instance.Faction) return; // Don't reward friendly kills

                var comp = killer.GetComp<CompIntelligence>();
                comp?.AddXP(StatType.CombatInstinct, 50f, "enemy_kill");
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Auto-retreat when severely wounded (Lv9+).
    /// Checks health every 120 ticks while drafted.
    /// </summary>
    // Combat health check is handled by MapComponent_CombatIntelligence, not a Harmony patch.

    /// <summary>
    /// Map component that handles periodic combat intelligence checks.
    /// More efficient than per-pawn per-tick checks.
    /// </summary>
    public class MapComponent_CombatIntelligence : MapComponent
    {
        public MapComponent_CombatIntelligence(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!LTSSettings.enableCombatInstinct) return;
            if (Find.TickManager.TicksGame % 120 != 0) return;

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || !pawn.Drafted) continue;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) continue;

                int level = comp.GetLevel(StatType.CombatInstinct);

                // Wounded retreat check (Lv9+)
                float retreatThreshold = CombatInstinctUtil.RetreatHealthPct(level);
                if (retreatThreshold > 0f)
                {
                    float healthPct = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                    if (healthPct < retreatThreshold)
                    {
                        // Undraft the pawn - they'll flee to safety
                        pawn.drafter.Drafted = false;

                        LTSLog.Decision(pawn, StatType.CombatInstinct, level, "WOUNDED_RETREAT",
                            "health=" + healthPct.ToString("P0"),
                            "undrafted for retreat",
                            "threshold=" + retreatThreshold.ToString("P0"));

                        Messages.Message(
                            "LTS_WoundedRetreat".Translate(pawn.LabelShort),
                            pawn, MessageTypeDefOf.NeutralEvent, false);
                    }
                }

                // Cover seeking (Lv3+): if not in cover, try to find cover
                if (CombatInstinctUtil.BasicCover(level) && pawn.Drafted)
                {
                    float currentCover = CoverUtility.TotalSurroundingCoverScore(pawn.Position, pawn.Map);
                    if (currentCover < 0.5f) // Not in good cover
                    {
                        IntVec3 coverCell = CombatInstinctUtil.FindBestCover(pawn, level);
                        if (coverCell.IsValid && coverCell != pawn.Position)
                        {
                            float newCover = CoverUtility.TotalSurroundingCoverScore(coverCell, pawn.Map);
                            if (newCover > currentCover + 0.3f) // Significantly better cover
                            {
                                // Suggest cover position (don't force - pawn is drafted, player has control)
                                // Only auto-move if Lv10+ (combat repositioning)
                                if (CombatInstinctUtil.CombatReposition(level))
                                {
                                    Job moveJob = JobMaker.MakeJob(JobDefOf.Goto, coverCell);
                                    moveJob.locomotionUrgency = LocomotionUrgency.Sprint;
                                    pawn.jobs.TryTakeOrderedJob(moveJob);

                                    LTSLog.Decision(pawn, StatType.CombatInstinct, level, "COVER_REPOSITION",
                                        "current_cover=" + currentCover.ToString("F1"),
                                        "moving to " + coverCell + " (cover=" + newCover.ToString("F1") + ")",
                                        "level=" + level);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
