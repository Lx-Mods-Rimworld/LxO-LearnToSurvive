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
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> jobTrackerPawn =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        public static bool Prepare() => LTSSettings.enableWorkAwareness;

        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                if (condition != JobCondition.Succeeded) return;

                Pawn pawn = jobTrackerPawn(__instance);
                if (pawn == null || pawn.Map == null) return;
                if (!pawn.IsColonistPlayerControlled) return;

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

                    Log.Message($"[LTS-Work] {pawn.LabelShort}: awarded 8 XP for completing {curJob.def.defName} (Lv{level})");

                    // Task chaining (Lv8+): after completing deconstruct/harvest/mine,
                    // queue the next nearby same-type job so we batch the work before hauling
                    // BUT: never chain if pawn has critical needs -- let ThinkTree handle food/rest/joy
                    if (WorkAwareness.TaskChaining(level))
                    {
                        bool needsCritical = false;
                        if (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < 0.35f)
                            needsCritical = true;
                        if (pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < 0.35f)
                            needsCritical = true;
                        if (pawn.needs?.joy != null && pawn.needs.joy.CurLevelPercentage < 0.15f)
                            needsCritical = true;

                        if (needsCritical)
                            Log.Message($"[LTS-Work] {pawn.LabelShort}: task chaining skipped, critical needs (food={pawn.needs?.food?.CurLevelPercentage:P0} rest={pawn.needs?.rest?.CurLevelPercentage:P0} joy={pawn.needs?.joy?.CurLevelPercentage:P0})");

                        Job chainedJob = needsCritical ? null : TryFindChainJob(pawn, curJob, level);
                        if (chainedJob != null)
                        {
                            // Pre-cleaning now handled by Patch_StartJob_PreClean (StartJob prefix pattern)
                            Log.Message($"[LTS-Work] {pawn.LabelShort}: task chaining {curJob.def.defName} -> {chainedJob.def.defName} at {chainedJob.targetA.Cell}");

                            __instance.jobQueue.EnqueueFirst(chainedJob);

                            LTSLog.Decision(pawn, StatType.WorkAwareness, level, "TASK_CHAIN",
                                "finished " + curJob.def.defName,
                                "queued next " + chainedJob.def.defName + " at " + chainedJob.targetA.Cell,
                                "chaining same-type work before hauling");

                            // Pre-cleaning handled by Patch_StartJob_PreClean
                        }
                        else if (!needsCritical)
                        {
                            Log.Message($"[LTS-Work] {pawn.LabelShort}: task chaining found no nearby {curJob.def.defName} work");
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
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                null,
                new System.Type[] { typeof(Bill), typeof(Pawn), typeof(Thing), typeof(List<ThingCount>) },
                null);

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
    /// Weather-gated snow/sand clearing (Lv10+):
    /// Don't clear snow while it's snowing, or sand during sandstorms.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_ClearSnowOrSand), nameof(WorkGiver_ClearSnowOrSand.ShouldSkip))]
    public static class Patch_SkipSnowInWeather
    {
        public static bool Prepare() => LTSSettings.enableWorkAwareness;

        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return true;
                if (pawn?.Map == null) return true;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return true;

                int level = comp.GetLevel(StatType.WorkAwareness);
                if (!WorkAwareness.WeatherAware(level)) return true;

                var weather = pawn.Map.weatherManager;
                if (weather.SnowRate > 0f || weather.RainRate > 0f)
                {
                    Log.Message($"[LTS-Work] {pawn.LabelShort}: skipping snow/sand clearing (weather: {pawn.Map.weatherManager.curWeather.defName}, snow={weather.SnowRate:F2}, rain={weather.RainRate:F2})");
                    __result = true; // skip = true
                    return false;    // don't run vanilla
                }
            }
            catch (Exception) { }
            return true;
        }
    }

    /// <summary>
    /// Pre-clean before precision work (Lv4+):
    /// When a pawn starts a DoBill job, insert a cleaning job first if there's filth
    /// near the workstation. Uses the Common Sense StartJob pattern:
    /// enqueue original job, start clean job first. Original job runs after clean finishes.
    /// If clean is interrupted, original job stays in queue.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_StartJob_PreClean
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> jobTrackerPawn =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        // Per-pawn cooldown: don't re-trigger pre-clean within 500 ticks of last trigger
        private static readonly Dictionary<int, int> lastCleanTick = new Dictionary<int, int>();

        public static bool Prepare() => LTSSettings.enableWorkAwareness;

        public static bool Prefix(Pawn_JobTracker __instance, Job newJob)
        {
            try
            {
                if (newJob == null || newJob.def != JobDefOf.DoBill) return true;

                Pawn pawn = jobTrackerPawn(__instance);
                if (pawn == null || pawn.Map == null) return true;
                if (!pawn.IsColonistPlayerControlled) return true;
                // Don't intercept if queue already has jobs (we already enqueued, or player-queued)
                if (__instance.jobQueue?.Count > 0)
                {
                    Log.Message($"[LTS-PreClean] {pawn.LabelShort}: skipped, queue has {__instance.jobQueue.Count} jobs");
                    return true;
                }

                // Cooldown: don't re-trigger within 500 ticks (~8 sec) of last pre-clean
                int curTick = Find.TickManager.TicksGame;
                if (lastCleanTick.TryGetValue(pawn.thingIDNumber, out int last) && curTick - last < 500)
                {
                    Log.Message($"[LTS-PreClean] {pawn.LabelShort}: skipped, cooldown ({curTick - last} ticks ago)");
                    return true;
                }

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return true;

                int level = comp.GetLevel(StatType.WorkAwareness);
                Log.Message($"[LTS-PreClean] {pawn.LabelShort}: DoBill detected, WorkAwareness={level}, checking clean...");
                if (!WorkAwareness.PreCleanCooking(level)) return true;

                // Check if this is precision work that benefits from cleaning
                bool shouldClean = false;
                if (newJob.bill?.recipe?.workSkill == SkillDefOf.Cooking)
                    shouldClean = true;
                if (WorkAwareness.PreCleanAllPrecision(level))
                {
                    var skill = newJob.bill?.recipe?.workSkill;
                    if (skill == SkillDefOf.Crafting || skill == SkillDefOf.Artistic
                        || skill == SkillDefOf.Intellectual)
                        shouldClean = true;
                }

                if (!shouldClean) return true;
                if (ModCompat.CommonSenseLoaded && LTSSettings.respectCommonSense) return true;

                // Check if pawn can clean
                if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Cleaning))
                {
                    Log.Message($"[LTS-PreClean] {pawn.LabelShort}: can't clean (disabled work type)");
                    return true;
                }

                Thing station = newJob.targetA.Thing;
                if (station == null || !station.Spawned) return true;

                // Path efficiency: only clean if pawn is reasonably close to station
                float pawnToStation = pawn.Position.DistanceTo(station.Position);
                if (pawnToStation > 10f)
                {
                    Log.Message($"[LTS-PreClean] {pawn.LabelShort}: too far from station ({pawnToStation:F0} tiles)");
                    return true;
                }

                var filthList = WorkAwareness.FindFilthToClean(pawn, station.Position, level);
                if (filthList == null || filthList.Count == 0)
                {
                    Log.Message($"[LTS-PreClean] {pawn.LabelShort}: no filth near {station.LabelShort}");
                    return true;
                }
                Log.Message($"[LTS-PreClean] {pawn.LabelShort}: found {filthList.Count} filth near {station.LabelShort}");

                // Pick filth closest to station (least detour)
                filthList.SortBy(f => f.Position.DistanceTo(station.Position));
                Thing filth = filthList[0];

                // Triangle inequality: skip if cleaning adds >30% travel
                float pawnToFilth = pawn.Position.DistanceTo(filth.Position);
                float filthToStation = filth.Position.DistanceTo(station.Position);
                if (pawnToStation > 3f && pawnToFilth + filthToStation > pawnToStation * 1.3f)
                    return true;

                if (!pawn.CanReserve(filth)) return true;

                // Common Sense pattern: enqueue original DoBill, then enqueue clean in front.
                // When clean finishes, pawn dequeues DoBill automatically.
                Job cleanJob = JobMaker.MakeJob(JobDefOf.Clean, filth);

                // Put the original DoBill in queue, then the clean job in front
                __instance.jobQueue.EnqueueFirst(newJob, null);
                // Enqueue clean in front -- it runs first, then DoBill dequeues
                __instance.jobQueue.EnqueueFirst(cleanJob, null);

                // Set cooldown so we don't re-trigger when DoBill dequeues
                lastCleanTick[pawn.thingIDNumber] = curTick;

                LTSLog.Decision(pawn, StatType.WorkAwareness, level, "PRE_CLEAN",
                    "cleaning before " + newJob.def.defName + " at " + station.LabelShort,
                    "filth " + filth.Position.DistanceTo(station.Position).ToString("F1") + " cells from station",
                    "StartJob pattern");

                return false; // Block original StartJob; TryFindAndStartJob will pick up queue
            }
            catch (Exception ex)
            {
                LTSLog.Error("StartJob pre-clean patch failed", ex);
            }
            return true;
        }
    }

    /// <summary>
    /// Need check before starting long work (Lv9+):
    /// Don't start a bill if hunger/rest is critical.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_DoBill_NeedCheck
    {
        public static bool Prepare() => LTSSettings.enableWorkAwareness;

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
                    Log.Message($"[LTS-Work] {pawn.LabelShort}: deferring work due to needs (hungry={tooHungry}, tired={tooTired}, food={pawn.needs?.food?.CurLevelPercentage:P0}, rest={pawn.needs?.rest?.CurLevelPercentage:P0})");
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
    /// Perishable ingredient priority: when a DoBill job is created,
    /// reorder the ingredient queue to pick up perishable items first.
    /// Requires WorkAwareness Lv7+ (same threshold as HaulingSense perishables).
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    [HarmonyAfter("Lexxers.LearnToSurvive")]
    public static class Patch_DoBill_PerishableIngredients
    {
        public static bool Prepare() => LTSSettings.enableWorkAwareness;

        [HarmonyPriority(Priority.Low)] // Run after NeedCheck
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (!LTSSettings.enableWorkAwareness) return;
                if (__result == null || pawn == null) return;
                if (__result.def != JobDefOf.DoBill) return;
                if (__result.targetQueueB == null || __result.targetQueueB.Count <= 1) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.WorkAwareness);
                if (level < 7) return; // Same threshold as perishable hauling

                // Sort ingredients: perishable first (by ticks until rot), then by distance
                bool anyPerishable = false;
                for (int i = 0; i < __result.targetQueueB.Count; i++)
                {
                    Thing t = __result.targetQueueB[i].Thing;
                    if (t?.TryGetComp<CompRottable>() != null)
                    {
                        anyPerishable = true;
                        break;
                    }
                }

                if (!anyPerishable) return;

                Log.Message($"[LTS-Work] {pawn.LabelShort}: reordering {__result.targetQueueB.Count} ingredients, perishables first (Lv{level})");

                // Capture into locals (ref params can't be used in lambdas)
                var queue = __result.targetQueueB;
                var counts = __result.countQueue;

                // Build parallel sort keys
                var indices = Enumerable.Range(0, queue.Count).ToList();
                indices.Sort((a, b) =>
                {
                    Thing ta = queue[a].Thing;
                    Thing tb = queue[b].Thing;
                    var rotA = ta?.TryGetComp<CompRottable>();
                    var rotB = tb?.TryGetComp<CompRottable>();
                    int ticksA = rotA != null ? rotA.TicksUntilRotAtCurrentTemp : int.MaxValue;
                    int ticksB = rotB != null ? rotB.TicksUntilRotAtCurrentTemp : int.MaxValue;
                    return ticksA.CompareTo(ticksB);
                });

                var sortedTargets = new List<LocalTargetInfo>(indices.Count);
                var sortedCounts = counts != null
                    ? new List<int>(indices.Count) : null;

                foreach (int idx in indices)
                {
                    sortedTargets.Add(queue[idx]);
                    sortedCounts?.Add(counts[idx]);
                }

                __result.targetQueueB = sortedTargets;
                if (sortedCounts != null) __result.countQueue = sortedCounts;
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

                // HaulToContainer: targetA = resource being hauled, targetB = frame/container
                Thing resource = __result.targetA.Thing;
                if (resource == null || !resource.Spawned) return;

                // Count how many nearby frames need this same material
                int originalCount = __result.count;
                if (originalCount <= 0) return;

                int totalNeeded = originalCount;
                Thing targetFrame = __result.targetB.Thing;
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

                    Log.Message($"[LTS-Work] {pawn.LabelShort}: batching construction delivery - carrying {newCount} {resource.def.label} instead of {originalCount} ({nearbyFrames} nearby frames)");
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
