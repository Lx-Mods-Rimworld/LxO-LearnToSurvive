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
                    if (WorkAwareness.TaskChaining(level))
                    {
                        Job chainedJob = TryFindChainJob(pawn, curJob, level);
                        if (chainedJob != null)
                        {
                            __instance.jobQueue.EnqueueFirst(chainedJob);

                            LTSLog.Decision(pawn, StatType.WorkAwareness, level, "TASK_CHAIN",
                                "finished " + curJob.def.defName,
                                "queued next " + chainedJob.def.defName + " at " + chainedJob.targetA.Cell,
                                "chaining same-type work before hauling");
                        }
                        else
                        {
                            // No more chaining -- restore bill store mode if we changed it
                            RestoreBillStoreMode(curJob.bill);
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

        // Track original bill store modes so we can restore after chaining
        private static Dictionary<int, BillStoreModeDef> originalStoreModes = new Dictionary<int, BillStoreModeDef>();

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

                // Find ingredients for the next iteration via WorkGiver_DoBill
                var method = typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredients",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (method == null) return null;

                var chosen = new List<ThingCount>();
                object[] args = new object[] { bill, pawn, workstation, chosen };
                bool found = (bool)method.Invoke(null, args);

                if (!found || chosen.Count == 0) return null;

                // Create the DoBill job
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

                // Force DropOnFloor so products accumulate near the stove
                // Save original mode so we can restore when chaining ends
                int billID = bill.GetHashCode();
                if (!originalStoreModes.ContainsKey(billID))
                    originalStoreModes[billID] = bill.GetStoreMode();
                bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor);

                return newJob;
            }
            catch (Exception ex)
            {
                LTSLog.Error("TryChainBill failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Restore bill store mode after crafting chain ends.
        /// </summary>
        private static void RestoreBillStoreMode(Bill bill)
        {
            if (bill == null) return;
            int billID = bill.GetHashCode();
            if (originalStoreModes.TryGetValue(billID, out BillStoreModeDef original))
            {
                bill.SetStoreMode(original);
                originalStoreModes.Remove(billID);
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
    /// Modify work scanner to prefer nearby work items for intelligent pawns.
    /// This affects how WorkGiver_Scanner picks potential work targets.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.PotentialWorkThingsGlobal))]
    public static class Patch_WorkThings_Proximity
    {
        public static void Postfix(WorkGiver_Scanner __instance, Pawn pawn, ref IEnumerable<Thing> __result)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                if (pawn == null || __result == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.WorkAwareness);
                if (level <= 0) return;

                float radius = WorkAwareness.GetLocalScanRadius(level);
                if (radius <= 0f) return;

                // Sort by distance to pawn
                IntVec3 pos = pawn.Position;
                var items = __result.ToList();
                if (items.Count <= 1) return;

                items.Sort((a, b) =>
                {
                    float distA = a.Position.DistanceTo(pos);
                    float distB = b.Position.DistanceTo(pos);

                    // Workstation loyalty bonus (Lv12+)
                    if (WorkAwareness.WorkstationLoyalty(level))
                    {
                        if (WorkAwareness.IsPreferredStation(comp, a)) distA *= 0.5f;
                        if (WorkAwareness.IsPreferredStation(comp, b)) distB *= 0.5f;
                    }

                    // Passion leaning (Lv15+): slight preference for passion-matching work
                    if (WorkAwareness.PassionLeaning(level) && __instance.def?.workType?.relevantSkills != null)
                    {
                        foreach (var skill in __instance.def.workType.relevantSkills)
                        {
                            var pawnSkill = pawn.skills?.GetSkill(skill);
                            if (pawnSkill != null && pawnSkill.passion != Passion.None)
                            {
                                distA *= 0.8f;
                                distB *= 0.8f;
                            }
                        }
                    }

                    return distA.CompareTo(distB);
                });

                __result = items;
            }
            catch (Exception ex)
            {
                LTSLog.Error("WorkThings proximity patch failed", ex);
            }
        }
    }

    /// <summary>
    /// Pre-clean before precision work (cooking, crafting, research, art).
    /// Inject a cleaning toil before the main work toil.
    /// </summary>
    // Manually patched in HarmonyInit - JobDriver_DoBill.MakeNewToils
    public static class Patch_DoBill_PreClean
    {
        public static void Postfix(JobDriver_DoBill __instance, ref IEnumerable<Toil> __result)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                if (ModCompat.CommonSenseLoaded && LTSSettings.respectCommonSense) return;

                Pawn pawn = __instance.pawn;
                if (pawn == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.WorkAwareness);
                if (!WorkAwareness.PreCleanCooking(level)) return;

                // Check if this is precision work
                var bill = __instance.job?.bill;
                if (bill == null) return;

                bool isPrecision = false;
                if (bill.recipe?.workSkill == SkillDefOf.Cooking) isPrecision = true;
                if (level >= 5) // Pre-clean for all precision work
                {
                    if (bill.recipe?.workSkill == SkillDefOf.Crafting) isPrecision = true;
                    if (bill.recipe?.workSkill == SkillDefOf.Artistic) isPrecision = true;
                    if (bill.recipe?.workSkill == SkillDefOf.Intellectual) isPrecision = true;
                }

                if (!isPrecision) return;

                // Prepend cleaning toils
                var toils = __result.ToList();

                Toil cleanToil = new Toil();
                cleanToil.initAction = () =>
                {
                    Thing station = __instance.job.targetA.Thing;
                    if (station == null) return;

                    // Record workstation use for loyalty
                    if (WorkAwareness.WorkstationLoyalty(level))
                        WorkAwareness.RecordStationUse(comp, station);

                    var filth = WorkAwareness.FindFilthToClean(pawn, station.Position, level);
                    if (filth == null || filth.Count == 0) return;

                    // Clean up to 3 filth items
                    int cleaned = 0;
                    foreach (var f in filth.Take(3))
                    {
                        if (f.Destroyed) continue;
                        f.Destroy();
                        cleaned++;
                    }

                    if (cleaned > 0)
                    {
                        comp.AddXP(StatType.WorkAwareness, 4f, "pre_clean");
                        LTSLog.Decision(pawn, StatType.WorkAwareness, level, "PRE_TASK_CLEAN",
                            "Cleaned " + cleaned + " filth before " + (bill.recipe?.label ?? "work"),
                            "cleaning", "level=" + level);
                    }
                };
                cleanToil.defaultCompleteMode = ToilCompleteMode.Instant;

                toils.Insert(0, cleanToil);
                __result = toils;
            }
            catch (Exception ex)
            {
                LTSLog.Error("DoBill pre-clean patch failed", ex);
            }
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
    /// Workstation speed bonus for loyal workers (Lv12+).
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class Patch_WorkSpeed_StationBonus
    {
        public static void Postfix(StatWorker __instance, StatRequest req, ref float __result)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                StatDef stat = Traverse.Create(__instance).Field("stat").GetValue<StatDef>();
                if (stat != StatDefOf.WorkSpeedGlobal) return;
                if (!req.HasThing || !(req.Thing is Pawn pawn)) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.WorkAwareness);
                float bonus = WorkAwareness.WorkstationSpeedBonus(level);
                if (bonus <= 0f) return;

                // Check if at preferred station
                var curJob = pawn.CurJob;
                if (curJob?.targetA.Thing != null && WorkAwareness.IsPreferredStation(comp, curJob.targetA.Thing))
                {
                    __result += bonus;
                }
            }
            catch (Exception) { }
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
