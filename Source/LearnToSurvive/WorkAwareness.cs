using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    public static class WorkAwareness
    {
        // Local scan radius after completing a task (Lv1: 10, Lv2: 20, Lv3+: 20 with cross-type)
        public static float GetLocalScanRadius(int level)
        {
            if (level <= 0) return 0f;
            if (level == 1) return 10f;
            return 20f;
        }

        public static bool CrossTypeLocal(int level) => level >= 3;
        public static bool PreCleanCooking(int level) => level >= 4;
        public static bool PreCleanAllPrecision(int level) => level >= 5;
        public static float CleanRadius(int level) => level >= 5 ? 5f : 3f;
        public static bool GatherAllIngredients(int level) => level >= 6;
        public static bool TaskChaining(int level) => level >= 8;
        public static bool NeedCheckBeforeWork(int level) => level >= 9;
        public static bool WeatherAware(int level) => level >= 10;
        public static bool UrgencyDetection(int level) => level >= 11;
        public static bool WorkstationLoyalty(int level) => level >= 12;
        public static float WorkstationSpeedBonus(int level) => level >= 20 ? 0.08f : (level >= 12 ? 0.03f : 0f);
        public static bool ResourceAware(int level) => level >= 13;
        public static bool BottleneckRelief(int level) => level >= 14;
        public static bool PassionLeaning(int level) => level >= 15;
        public static bool WorkflowBatching(int level) => level >= 16;
        public static bool AnticipatoryWork(int level) => level >= 17;
        public static bool TeamAware(int level) => level >= 18;

        /// <summary>
        /// Check if there's filth near a work position that should be cleaned first.
        /// </summary>
        public static List<Thing> FindFilthToClean(Pawn pawn, IntVec3 workPos, int level)
        {
            if (!PreCleanCooking(level)) return null;

            float radius = CleanRadius(level);
            var filthList = new List<Thing>();

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(workPos, radius, true))
            {
                if (!cell.InBounds(pawn.Map)) continue;
                var things = cell.GetThingList(pawn.Map);
                foreach (var thing in things)
                {
                    if (thing is Filth filth && !filth.Destroyed && pawn.CanReserve(filth))
                    {
                        filthList.Add(filth);
                    }
                }
            }

            return filthList.Count > 0 ? filthList : null;
        }

        /// <summary>
        /// Check if a work type is "precision work" that benefits from a clean environment.
        /// </summary>
        public static bool IsPrecisionWork(JobDef jobDef, int level)
        {
            if (jobDef == null) return false;
            // Level 4: just cooking
            if (jobDef == JobDefOf.DoBill)
                return true; // We'll refine this in the patch based on the bill's workSkill
            return false;
        }

        /// <summary>
        /// For workstation loyalty: record that a pawn used a specific station.
        /// </summary>
        public static void RecordStationUse(CompIntelligence comp, Thing station)
        {
            if (comp == null || station == null) return;
            string key = station.def.defName;
            comp.preferredStations[key] = station.thingIDNumber;
        }

        /// <summary>
        /// Check if this is the pawn's preferred station.
        /// </summary>
        public static bool IsPreferredStation(CompIntelligence comp, Thing station)
        {
            if (comp == null || station == null) return false;
            string key = station.def.defName;
            return comp.preferredStations.TryGetValue(key, out int id) && id == station.thingIDNumber;
        }

        /// <summary>
        /// Check if the pawn is currently working at their preferred station.
        /// Used by StatPart_WorkstationLoyalty.
        /// </summary>
        public static bool IsPreferredStation(Pawn pawn, CompIntelligence comp)
        {
            if (pawn == null || comp == null) return false;
            var curJob = pawn.CurJob;
            if (curJob?.targetA.Thing == null) return false;
            return IsPreferredStation(comp, curJob.targetA.Thing);
        }
    }

    // ========== HARMONY PATCHES ==========

    /// <summary>
    /// Award work XP when a job completes successfully.
    /// Also handles task chaining: after finishing work, look for nearby related work.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_EndJob_WorkXP
    {
        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                if (condition != JobCondition.Succeeded) return;

                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawn == null || pawn.Map == null) return;

                var curJob = __instance.curJob;
                if (curJob == null) return;

                // Award XP for work completion (not hauling)
                if (curJob.def != JobDefOf.HaulToCell && curJob.def != JobDefOf.HaulToContainer
                    && curJob.def != JobDefOf.Wait && curJob.def != JobDefOf.Wait_MaintainPosture
                    && curJob.def != JobDefOf.GotoWander && curJob.def != JobDefOf.GotoSafeTemperature)
                {
                    var comp = pawn.GetComp<CompIntelligence>();
                    if (comp == null) return;
                    comp.AddXP(StatType.WorkAwareness, 8f, "work_complete");

                    int level = comp.GetLevel(StatType.WorkAwareness);

                    // Task chaining (Lv8+): after completing deconstruct/harvest/mine,
                    // queue the next nearby same-type job so we batch the work before hauling
                    // BUT: never chain if pawn has critical needs -- let ThinkTree handle food/rest/joy
                    if (WorkAwareness.TaskChaining(level))
                    {
                        bool needsCritical = false;
                        if (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < 0.25f)
                            needsCritical = true;
                        if (pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < 0.25f)
                            needsCritical = true;
                        if (pawn.needs?.joy != null && pawn.needs.joy.CurLevelPercentage < 0.05f)
                            needsCritical = true;

                        Job chainedJob = needsCritical ? null : TryFindChainJob(pawn, curJob, level);
                        if (chainedJob != null)
                        {
                            // Pre-clean before chained DoBill (Lv4+)
                            if (WorkAwareness.PreCleanCooking(level) && chainedJob.def == JobDefOf.DoBill)
                            {
                                bool shouldClean = false;
                                if (chainedJob.bill?.recipe?.workSkill == SkillDefOf.Cooking)
                                    shouldClean = true;
                                if (WorkAwareness.PreCleanAllPrecision(level))
                                {
                                    var skill = chainedJob.bill?.recipe?.workSkill;
                                    if (skill == SkillDefOf.Crafting || skill == SkillDefOf.Artistic
                                        || skill == SkillDefOf.Intellectual)
                                        shouldClean = true;
                                }

                                if (shouldClean && !(ModCompat.CommonSenseLoaded && LTSSettings.respectCommonSense))
                                {
                                    Thing station = chainedJob.targetA.Thing;
                                    if (station != null && station.Spawned)
                                    {
                                        var filthList = WorkAwareness.FindFilthToClean(pawn, station.Position, level);
                                        if (filthList != null && filthList.Count > 0)
                                        {
                                            Thing filth = filthList[0];
                                            if (pawn.CanReserve(filth))
                                            {
                                                Job cleanJob = JobMaker.MakeJob(JobDefOf.Clean, filth);
                                                __instance.jobQueue.EnqueueFirst(chainedJob);
                                                __instance.jobQueue.EnqueueFirst(cleanJob);

                                                LTSLog.Decision(pawn, StatType.WorkAwareness, level, "PRE_CLEAN",
                                                    "queued Clean before " + chainedJob.def.defName, "", "");

                                                // Skip normal enqueue below -- we already handled it
                                                goto doneChaining;
                                            }
                                        }
                                    }
                                }
                            }

                            __instance.jobQueue.EnqueueFirst(chainedJob);

                            LTSLog.Decision(pawn, StatType.WorkAwareness, level, "TASK_CHAIN",
                                "finished " + curJob.def.defName,
                                "queued next " + chainedJob.def.defName + " at " + chainedJob.targetA.Cell,
                                "chaining same-type work before hauling");

                            doneChaining:;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LTSLog.Error("EndJob work XP patch failed", ex);
            }
        }

        /// <summary>
        /// Find another nearby job of the same type to chain to.
        /// Works for: deconstruct, harvest, mine, cut plants, smooth.
        /// </summary>
        private static Job TryFindChainJob(Pawn pawn, Job finishedJob, int level)
        {
            // Determine what we just did and find more of it nearby
            float radius = level >= 16 ? 20f : 12f; // Workflow batching = wider radius
            Map map = pawn.Map;
            IntVec3 pos = pawn.Position;

            // Deconstruct -> find more things designated for deconstruction
            if (finishedJob.def == JobDefOf.Deconstruct)
            {
                return FindDesignatedJob(pawn, map, pos, radius,
                    DesignationDefOf.Deconstruct, JobDefOf.Deconstruct);
            }

            // Mine -> find more things designated for mining
            if (finishedJob.def == JobDefOf.Mine)
            {
                return FindDesignatedCellJob(pawn, map, pos, radius,
                    DesignationDefOf.Mine, JobDefOf.Mine);
            }

            // Harvest -> find more harvestable plants nearby
            if (finishedJob.def == JobDefOf.Harvest)
            {
                return FindDesignatedJob(pawn, map, pos, radius,
                    DesignationDefOf.HarvestPlant, JobDefOf.Harvest);
            }

            // CutPlant -> find more plants designated for cutting
            if (finishedJob.def == JobDefOf.CutPlant)
            {
                return FindDesignatedJob(pawn, map, pos, radius,
                    DesignationDefOf.CutPlant, JobDefOf.CutPlant);
            }

            // SmoothFloor
            if (finishedJob.def == JobDefOf.SmoothFloor)
            {
                return FindDesignatedCellJob(pawn, map, pos, radius,
                    DesignationDefOf.SmoothFloor, JobDefOf.SmoothFloor);
            }

            // DoBill (cooking, crafting) -> chain another iteration of the same bill
            if (finishedJob.def == JobDefOf.DoBill && finishedJob.bill != null)
            {
                return TryChainBill(pawn, finishedJob);
            }

            return null;
        }

        // Cached MethodInfo for TryFindBestBillIngredients (used by TryChainBill)
        private static readonly System.Reflection.MethodInfo tryFindBestBillIngredientsMethod =
            typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredients",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        /// <summary>
        /// Chain another iteration of the same crafting bill.
        /// Products drop on floor during chaining, then get batch-hauled after.
        /// </summary>
        private static Job TryChainBill(Pawn pawn, Job finishedJob)
        {
            try
            {
                Bill bill = finishedJob.bill;
                if (bill == null || bill.deleted) return null;

                // Bill still needs more iterations?
                if (bill is Bill_Production prod && !prod.ShouldDoNow()) return null;

                // Workstation still exists and usable?
                Thing workstation = finishedJob.targetA.Thing;
                if (workstation == null || workstation.Destroyed || !workstation.Spawned) return null;
                if (!pawn.CanReserve(workstation)) return null;

                // Use cached MethodInfo
                if (tryFindBestBillIngredientsMethod == null) return null;

                var chosen = new List<ThingCount>();
                object[] args = new object[] { bill, pawn, workstation, chosen };
                bool found = (bool)tryFindBestBillIngredientsMethod.Invoke(null, args);

                if (!found || chosen.Count == 0) return null;

                // Create the DoBill job -- do not modify bill store mode
                Job newJob = JobMaker.MakeJob(JobDefOf.DoBill, workstation);
                newJob.targetQueueB = new List<LocalTargetInfo>(chosen.Count);
                newJob.countQueue = new List<int>(chosen.Count);
                foreach (ThingCount tc in chosen)
                {
                    newJob.targetQueueB.Add(tc.Thing);
                    newJob.countQueue.Add(tc.Count);
                }
                newJob.bill = bill;
                newJob.haulMode = HaulMode.ToCellNonStorage;

                return newJob;
            }
            catch (Exception ex)
            {
                LTSLog.Error("TryChainBill failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Find a nearby thing with a specific designation and create a job for it.
        /// </summary>
        private static Job FindDesignatedJob(Pawn pawn, Map map, IntVec3 center,
            float radius, DesignationDef desDef, JobDef jobDef)
        {
            Thing best = null;
            float bestDist = radius + 1f;

            foreach (Designation des in map.designationManager.AllDesignations)
            {
                if (des.def != desDef) continue;
                if (des.target.Thing == null) continue;

                Thing t = des.target.Thing;
                if (t.Destroyed || !t.Spawned) continue;

                float dist = t.Position.DistanceTo(center);
                if (dist > radius || dist >= bestDist) continue;
                if (!pawn.CanReserve(t)) continue;

                best = t;
                bestDist = dist;
            }

            if (best == null) return null;
            Job job = JobMaker.MakeJob(jobDef, best);
            return job;
        }

        /// <summary>
        /// Find a nearby cell with a specific designation and create a job for it.
        /// </summary>
        private static Job FindDesignatedCellJob(Pawn pawn, Map map, IntVec3 center,
            float radius, DesignationDef desDef, JobDef jobDef)
        {
            IntVec3 bestCell = IntVec3.Invalid;
            float bestDist = radius + 1f;
            Thing bestThing = null;

            foreach (Designation des in map.designationManager.AllDesignations)
            {
                if (des.def != desDef) continue;
                if (!des.target.HasThing && des.target.Cell.IsValid)
                {
                    IntVec3 cell = des.target.Cell;
                    float dist = cell.DistanceTo(center);
                    if (dist > radius || dist >= bestDist) continue;
                    if (!pawn.CanReach(cell, PathEndMode.Touch, Danger.Some)) continue;

                    bestCell = cell;
                    bestDist = dist;
                }
                else if (des.target.HasThing)
                {
                    Thing t = des.target.Thing;
                    if (t.Destroyed || !t.Spawned) continue;
                    float dist = t.Position.DistanceTo(center);
                    if (dist > radius || dist >= bestDist) continue;
                    if (!pawn.CanReserve(t)) continue;

                    bestThing = t;
                    bestCell = t.Position;
                    bestDist = dist;
                }
            }

            if (!bestCell.IsValid) return null;
            if (bestThing != null)
                return JobMaker.MakeJob(jobDef, bestThing);
            return JobMaker.MakeJob(jobDef, bestCell);
        }
    }

    /// <summary>
    /// Need check before starting long work (Lv9+):
    /// Don't start a bill if hunger/rest is critical.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_DoBill_NeedCheck
    {
        public static void Postfix(WorkGiver_DoBill __instance, Pawn pawn, Thing thing, ref Job __result)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                if (__result == null || pawn == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.WorkAwareness);
                if (!WorkAwareness.NeedCheckBeforeWork(level)) return;

                // Check if hunger or rest is critically low
                bool tooHungry = pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < 0.2f;
                bool tooTired = pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < 0.2f;

                if (tooHungry || tooTired)
                {
                    LTSLog.Decision(pawn, StatType.WorkAwareness, level, "NEED_CHECK_DEFER",
                        "hunger=" + (pawn.needs?.food?.CurLevelPercentage ?? 1f).ToString("P0") +
                        " rest=" + (pawn.needs?.rest?.CurLevelPercentage ?? 1f).ToString("P0"),
                        "deferring work",
                        "critical need detected, level=" + level);
                    __result = null; // Cancel the work job, let need-satisfaction take over
                }
            }
            catch (Exception ex)
            {
                LTSLog.Error("DoBill need check patch failed", ex);
            }
        }
    }

    /// <summary>
    /// Construction batch delivery: when hauling materials to a frame/blueprint,
    /// carry a full stack (not just the minimum for one frame) so excess drops
    /// near the build site. Next delivery picks up nearby excess instead of
    /// walking back to stockpile. Requires Work Awareness >= 6.
    /// </summary>
    // Manually patched in HarmonyInit
    public static class Patch_ConstructDeliverResources
    {
        public static void Postfix(object __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                if (__result == null || pawn == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.WorkAwareness);
                if (!WorkAwareness.GatherAllIngredients(level)) return; // Lv6+

                // Only modify haul-to-frame jobs
                if (__result.def != JobDefOf.HaulToContainer) return;

                Thing resource = __result.targetB.Thing;
                if (resource == null || !resource.Spawned) return;

                // Count how many nearby frames need this same material
                int originalCount = __result.count;
                if (originalCount <= 0) return;

                int totalNeeded = originalCount;
                Thing targetFrame = __result.targetA.Thing;
                if (targetFrame == null) return;

                // Scan for more frames of same type nearby that need the same resource
                int nearbyFrames = 0;
                foreach (Thing thing in GenRadial.RadialDistinctThingsAround(
                    targetFrame.Position, pawn.Map, 12f, true))
                {
                    if (thing == targetFrame) continue;
                    if (thing.def != targetFrame.def) continue;

                    // Check if this frame needs the same material
                    Frame frame = thing as Frame;
                    if (frame == null) continue;

                    int countNeeded = frame.ThingCountNeeded(resource.def);
                    if (countNeeded > 0)
                    {
                        totalNeeded += countNeeded;
                        nearbyFrames++;
                    }

                    if (nearbyFrames >= 10) break; // Cap at 10 extra frames
                }

                if (nearbyFrames <= 0) return;

                // Increase haul count to cover multiple frames (capped by carry capacity and available stock)
                int carryCapacity = pawn.carryTracker?.MaxStackSpaceEver(resource.def) ?? resource.def.stackLimit;
                int newCount = Math.Min(totalNeeded, Math.Min(carryCapacity, resource.stackCount));

                if (newCount > originalCount)
                {
                    __result.count = newCount;

                    LTSLog.Decision(pawn, StatType.WorkAwareness, level, "CONSTRUCTION_BATCH",
                        nearbyFrames + " nearby frames need " + resource.def.label,
                        "carrying " + newCount + " instead of " + originalCount,
                        "WorkAwareness >= 6");
                }
            }
            catch (Exception ex)
            {
                LTSLog.Error("ConstructDeliverResources patch failed", ex);
            }
        }
    }
}
