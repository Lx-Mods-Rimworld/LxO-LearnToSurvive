using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    public static class HaulingSense
    {
        // Grab radius by level: Lv1=3, Lv2=6, Lv3+=6 (Lv3 adds mixed types)
        public static float GetGrabRadius(int level)
        {
            if (level <= 0) return 0f;
            if (level <= 1) return 12f;
            if (level <= 3) return 20f;
            if (level <= 6) return 30f;
            if (level <= 10) return 45f;
            return 60f; // Lv11+: entire base
        }

        public static bool UseInventory(int level) => level >= 4;
        public static bool PrioritizePerishables(int level) => level >= 7;
        public static bool BatchByDestination(int level) => level >= 8;
        public static bool ReturnTripAware(int level) => level >= 9;
        public static bool AvoidDuplicates(int level) => level >= 10;
        public static bool SmartUnloading(int level) => level >= 11;
        public static bool OpportunisticHaul(int level) => level >= 12;
        public static bool PrioritizeConstruction(int level) => level >= 13;
        public static bool PreStage(int level) => level >= 14;
        public static bool StockpileAware(int level) => level >= 15;
        public static bool CoordinateHaulers(int level) => level >= 16;

        // Proximity weight for job selection (Lv5-6: 15-30%)
        public static float GetProximityWeight(int level)
        {
            if (level < 5) return 0f;
            if (level == 5) return 0.15f;
            if (level == 6) return 0.30f;
            return 0.30f;
        }

        /// <summary>
        /// Find nearby items suitable for batching with the primary haul target.
        /// </summary>
        public static List<Thing> FindNearbyItems(Pawn pawn, Thing primary, int level)
        {
            var result = new List<Thing>();
            if (level <= 0 || pawn.Map == null) return result;

            float radius = GetGrabRadius(level);
            bool mixedTypes = level >= 3;
            bool useInv = UseInventory(level);

            IntVec3 storageCell = IntVec3.Invalid;
            if (!mixedTypes || BatchByDestination(level))
            {
                // Find where the primary item wants to go
                StoragePriority currentPriority = StoreUtility.CurrentStoragePriorityOf(primary);
                StoreUtility.TryFindBestBetterStoreCellFor(primary, pawn, pawn.Map, currentPriority,
                    pawn.Faction, out storageCell);
            }

            var candidates = GenRadial.RadialDistinctThingsAround(primary.Position, pawn.Map, radius, true)
                .Where(t => t != primary && t.def.EverHaulable && !t.IsForbidden(pawn)
                    && pawn.CanReserve(t) && !t.IsInValidBestStorage());

            // Apply claim checking at level 10+
            HashSet<int> claimedItems = null;
            if (AvoidDuplicates(level))
                claimedItems = GetClaimedHaulItems(pawn.Map, pawn);

            foreach (Thing candidate in candidates)
            {
                if (useInv && !MassUtility.CanEverCarryAnything(pawn)) break;

                // Check if another pawn is already going for this
                if (claimedItems != null && claimedItems.Contains(candidate.thingIDNumber))
                    continue;

                if (!mixedTypes && candidate.def != primary.def)
                    continue;

                // If batching by destination, check same stockpile target
                if (BatchByDestination(level) && storageCell.IsValid)
                {
                    IntVec3 candidateStorage;
                    StoreUtility.TryFindBestBetterStoreCellFor(candidate, pawn, pawn.Map,
                        StoreUtility.CurrentStoragePriorityOf(candidate), pawn.Faction, out candidateStorage);
                    // Allow if going to a nearby stockpile (within 10 tiles of primary destination)
                    if (candidateStorage.IsValid && candidateStorage.DistanceTo(storageCell) > 10f)
                        continue;
                }

                result.Add(candidate);

                // Inventory mode: limit by carry capacity
                if (useInv)
                {
                    float totalMass = result.Sum(t => t.GetStatValue(StatDefOf.Mass)) + primary.GetStatValue(StatDefOf.Mass);
                    if (totalMass > MassUtility.Capacity(pawn) * 0.9f)
                        break;
                }
            }

            // Sort: perishables first if level 7+
            if (PrioritizePerishables(level))
            {
                result.Sort((a, b) =>
                {
                    bool aPerish = a.TryGetComp<CompRottable>() != null;
                    bool bPerish = b.TryGetComp<CompRottable>() != null;
                    if (aPerish != bPerish) return aPerish ? -1 : 1;
                    return a.Position.DistanceTo(primary.Position)
                        .CompareTo(b.Position.DistanceTo(primary.Position));
                });
            }

            return result;
        }

        /// <summary>
        /// Get items currently targeted by other hauling pawns on this map.
        /// </summary>
        private static HashSet<int> GetClaimedHaulItems(Map map, Pawn excludePawn)
        {
            var claimed = new HashSet<int>();
            foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn == excludePawn) continue;
                var curJob = pawn.CurJob;
                if (curJob == null) continue;
                if (curJob.def == JobDefOf.HaulToCell || curJob.def == JobDefOf.HaulToContainer
                    || (LTSDefOf.LTS_SmartHaul != null && curJob.def == LTSDefOf.LTS_SmartHaul))
                {
                    if (curJob.targetA.HasThing)
                        claimed.Add(curJob.targetA.Thing.thingIDNumber);
                }
            }
            return claimed;
        }

        /// <summary>
        /// For construction staging (Lv13+): find items needed by active blueprints/frames.
        /// </summary>
        public static bool IsNeededForConstruction(Thing t, Map map)
        {
            if (map == null) return false;
            foreach (var blueprint in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
            {
                if (blueprint is Blueprint bp && bp.def.entityDefToBuild is ThingDef builtDef)
                {
                    var costList = builtDef.costList;
                    if (costList != null && costList.Any(tc => tc.thingDef == t.def))
                        return true;
                    if (builtDef.costStuffCount > 0 && t.def.IsStuff)
                        return true;
                }
            }
            foreach (var frame in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                if (frame is Frame f && f.def.entityDefToBuild is ThingDef frameDef)
                {
                    var costList = frameDef.costList;
                    if (costList != null && costList.Any(tc => tc.thingDef == t.def))
                        return true;
                    if (frameDef.costStuffCount > 0 && t.def.IsStuff)
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Custom JobDriver for intelligent hauling. Uses vanilla carry/place toils.
    /// Pawn physically walks to nearby same-type items to merge stacks.
    /// TargetA = item to haul, TargetB = destination cell, TargetC = next nearby item.
    /// </summary>
    public class JobDriver_SmartHaul : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex DestInd = TargetIndex.B;
        private const TargetIndex ExtraInd = TargetIndex.C;

        private List<Thing> nearbyItems = new List<Thing>();
        private int nearbyIndex;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail conditions only BEFORE pickup
            this.FailOnDestroyedOrNull(ItemInd);
            this.FailOnBurningImmobile(ItemInd);

            // 1. Go to the primary item
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            // 2. Pick up using vanilla's StartCarryThing
            yield return Toils_Haul.StartCarryThing(ItemInd, false, true);

            // 3. Award XP + find nearby items to walk to
            Toil postPickup = new Toil();
            postPickup.initAction = () =>
            {
                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;
                comp.AddXP(StatType.HaulingSense, 10f, "haul_pickup");

                int level = comp.GetLevel(StatType.HaulingSense);
                if (level < 1) return;

                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null) return;

                int stackSpace = carried.def.stackLimit - carried.stackCount;
                if (stackSpace <= 0) return;

                // Calculate mass capacity remaining
                float massCarried = MassUtility.GearAndInventoryMass(pawn) + carried.GetStatValue(StatDefOf.Mass) * carried.stackCount;
                float massCapacity = MassUtility.Capacity(pawn);
                float massRemaining = massCapacity - massCarried;
                float massPerItem = carried.def.GetStatValueAbstract(StatDefOf.Mass);

                if (massRemaining <= 0f) return;

                // How many more items can we carry by mass?
                // If item has zero mass, mass is not a constraint
                int massSpace = massPerItem > 0f ? (int)(massRemaining / massPerItem) : stackSpace;
                int spaceRemaining = Math.Min(stackSpace, massSpace);
                if (spaceRemaining <= 0) return;

                // Build list of nearby same-type items to walk to.
                // Only limit: carry capacity (min of stack limit and mass limit).
                float radius = HaulingSense.GetGrabRadius(level);
                nearbyItems.Clear();
                nearbyIndex = 0;

                foreach (Thing nearby in GenRadial.RadialDistinctThingsAround(
                    pawn.Position, pawn.Map, radius, true))
                {
                    if (spaceRemaining <= 0) break;
                    if (nearby == carried) continue;
                    if (nearby.def != carried.def) continue;
                    if (nearby.Destroyed || !nearby.Spawned) continue;
                    if (nearby.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(nearby)) continue;
                    nearbyItems.Add(nearby);
                    spaceRemaining -= Math.Min(nearby.stackCount, spaceRemaining);
                }
            };
            postPickup.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return postPickup;

            // 4. Walk-and-merge loop: go to each nearby item and absorb it
            // Forward-declare findCell so checkNextItem can jump to it
            Toil findCell = new Toil();

            // -- Walk to the next nearby item --
            Toil gotoNextItem = new Toil();
            gotoNextItem.initAction = () =>
            {
                Thing target = job.targetC.Thing;
                if (target == null || target.Destroyed || !target.Spawned)
                {
                    nearbyIndex++;
                    JumpToToil(findCell);
                }
                else
                {
                    pawn.pather.StartPath(target, PathEndMode.ClosestTouch);
                }
            };
            gotoNextItem.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            gotoNextItem.AddFailCondition(() =>
            {
                Thing t = job.targetC.Thing;
                return t == null || t.Destroyed;
            });

            // -- Absorb the item into carried stack --
            Toil mergeItem = new Toil();
            mergeItem.initAction = () =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                Thing target = job.targetC.Thing;
                if (carried == null || target == null || target.Destroyed || !target.Spawned)
                {
                    nearbyIndex++;
                    JumpToToil(findCell);
                    return;
                }

                int stackSpace = carried.def.stackLimit - carried.stackCount;

                // Also check mass capacity
                float massCarried = MassUtility.GearAndInventoryMass(pawn)
                    + carried.GetStatValue(StatDefOf.Mass) * carried.stackCount;
                float massRemaining = MassUtility.Capacity(pawn) - massCarried;
                float massPerItem = carried.def.GetStatValueAbstract(StatDefOf.Mass);
                int massSpace = massPerItem > 0f ? (int)(massRemaining / massPerItem) : stackSpace;

                int space = Math.Min(stackSpace, massSpace);
                int take = Math.Min(target.stackCount, space);
                if (take <= 0)
                {
                    nearbyIndex++;
                    JumpToToil(findCell); // No more capacity, go to storage
                    return;
                }

                carried.stackCount += take;
                if (take >= target.stackCount)
                    target.Destroy();
                else
                    target.stackCount -= take;

                nearbyIndex++;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp != null)
                {
                    comp.AddXP(StatType.HaulingSense, 3f, "stack_merge");
                    LTSLog.Decision(pawn, StatType.HaulingSense,
                        comp.GetLevel(StatType.HaulingSense), "STACK_MERGE",
                        "merged " + take + "x " + carried.def.label,
                        "walked to nearby stack and merged",
                        "remaining_space=" + (carried.def.stackLimit - carried.stackCount));
                }
            };
            mergeItem.defaultCompleteMode = ToilCompleteMode.Instant;

            // Forward-declare wait toil so checkNextItem can reference it
            Toil waitForProduction = new Toil();
            waitForProduction.defaultCompleteMode = ToilCompleteMode.Delay;
            waitForProduction.defaultDuration = 180; // ~3 seconds game time

            // -- Check: set up the next target or jump to findCell --
            Toil checkNextItem = new Toil();
            checkNextItem.initAction = () =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null)
                {
                    JumpToToil(findCell);
                    return;
                }

                // Check both stack limit AND mass capacity
                int stackSpace = carried.def.stackLimit - carried.stackCount;
                float massCarried = MassUtility.GearAndInventoryMass(pawn)
                    + carried.GetStatValue(StatDefOf.Mass) * carried.stackCount;
                float massRemaining = MassUtility.Capacity(pawn) - massCarried;
                float massPerItem = carried.def.GetStatValueAbstract(StatDefOf.Mass);
                int massSpace = massPerItem > 0f ? (int)(massRemaining / massPerItem) : stackSpace;
                int canCarryMore = Math.Min(stackSpace, massSpace);

                if (canCarryMore <= 0)
                {
                    JumpToToil(findCell);
                    return;
                }

                // RESCAN from current position every time (not a stale list).
                // The pawn may have walked to a new area with different nearby items.
                var comp = pawn.GetComp<CompIntelligence>();
                int level = comp?.GetLevel(StatType.HaulingSense) ?? 0;
                float radius = HaulingSense.GetGrabRadius(level);

                Thing bestNext = null;
                float bestDist = float.MaxValue;

                foreach (Thing nearby in GenRadial.RadialDistinctThingsAround(
                    pawn.Position, pawn.Map, radius, true))
                {
                    if (nearby == carried) continue;
                    if (nearby.def != carried.def) continue;
                    if (nearby.Destroyed || !nearby.Spawned) continue;
                    if (nearby.IsForbidden(pawn)) continue;
                    if (!pawn.CanReserve(nearby)) continue;

                    float dist = nearby.Position.DistanceTo(pawn.Position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestNext = nearby;
                    }
                }

                if (bestNext != null)
                {
                    job.targetC = bestNext;
                    pawn.Reserve(bestNext, job);
                    JumpToToil(gotoNextItem);
                    return;
                }

                // Nothing nearby right now. Check if a worker is producing more nearby.
                if (IsWorkerProducingNearby(pawn, carried.def, radius))
                {
                    // Worker is active -- wait briefly then rescan
                    JumpToToil(waitForProduction);
                    return;
                }

                // Nobody producing, go to storage
                JumpToToil(findCell);
            };
            checkNextItem.defaultCompleteMode = ToilCompleteMode.Instant;

            // Post-wait: rescan after waiting
            Toil postWait = new Toil();
            postWait.initAction = () =>
            {
                JumpToToil(checkNextItem);
            };
            postWait.defaultCompleteMode = ToilCompleteMode.Instant;

            // After merging, loop back to check for more
            Toil loopBack = new Toil();
            loopBack.initAction = () =>
            {
                JumpToToil(checkNextItem);
            };
            loopBack.defaultCompleteMode = ToilCompleteMode.Instant;

            // Yield all toils in execution order
            yield return checkNextItem;       // 0: check for next item or wait or go to storage
            yield return gotoNextItem;        // 1: walk to next item
            yield return mergeItem;           // 2: absorb item
            yield return loopBack;            // 3: jump back to check
            yield return waitForProduction;   // 4: wait 3 seconds
            yield return postWait;            // 5: rescan after wait

            // 5. Find best storage cell (findCell was forward-declared above)
            findCell.initAction = () =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                IntVec3 storeCell = FindCellWithRoom(carried, pawn);
                if (!storeCell.IsValid)
                {
                    // No valid storage with room -- just drop it here
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                job.targetB = storeCell;
            };
            findCell.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findCell;

            // 6. Carry to storage
            yield return Toils_Goto.GotoCell(DestInd, PathEndMode.OnCell);

            // 7. Place -- robust handler that deals with full stacks
            Toil placeToil = new Toil();
            placeToil.initAction = () =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                IntVec3 destCell = job.targetB.Cell;

                // Double-check the cell has room before placing
                if (!CellHasRoom(destCell, carried, pawn.Map))
                {
                    Log.Warning("[LearnToSurvive] " + pawn.LabelShort + " arrived at full cell " + destCell
                        + " - finding new cell for " + carried.stackCount + "x " + carried.def.label);
                    destCell = FindCellWithRoom(carried, pawn);
                    if (destCell.IsValid)
                    {
                        // Walk to the new cell instead
                        job.targetB = destCell;
                        JumpToToil(findCell); // re-run findCell which re-routes to storage
                        return;
                    }
                }

                // Try to place at destination
                if (pawn.carryTracker.TryDropCarriedThing(destCell, ThingPlaceMode.Direct, out Thing dropped))
                {
                    if (dropped != null && dropped.Spawned)
                        dropped.SetForbidden(false, false);
                }
                else
                {
                    // Direct placement failed -- try nearby
                    if (pawn.carryTracker.TryDropCarriedThing(destCell, ThingPlaceMode.Near, out dropped))
                    {
                        if (dropped != null && dropped.Spawned)
                            dropped.SetForbidden(false, false);
                    }
                    else
                    {
                        // Last resort: drop at feet
                        pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out dropped);
                    }
                }
            };
            placeToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return placeToil;
        }

        /// <summary>
        /// Find a storage cell that actually has room for the carried item.
        /// Vanilla TryFindBestBetterStoreCellFor can return full cells.
        /// </summary>
        public static IntVec3 FindCellWithRoom(Thing carried, Pawn pawn)
        {
            Map map = pawn.Map;
            StoragePriority currentPri = StoreUtility.CurrentStoragePriorityOf(carried);

            // First: try vanilla's method
            IntVec3 vanillaCell;
            if (StoreUtility.TryFindBestBetterStoreCellFor(carried, pawn, map,
                currentPri, pawn.Faction, out vanillaCell))
            {
                // Verify it actually has room
                if (CellHasRoom(vanillaCell, carried, map))
                    return vanillaCell;
            }

            // Vanilla cell was full -- search all valid storage cells for one with room
            foreach (SlotGroup slotGroup in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (slotGroup.Settings.Priority < currentPri) continue;

                foreach (IntVec3 cell in slotGroup.CellsList)
                {
                    if (!CellHasRoom(cell, carried, map)) continue;
                    if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                    if (cell.IsForbidden(pawn)) continue;
                    return cell;
                }
            }

            return IntVec3.Invalid;
        }

        /// <summary>
        /// Check if a cell can accept at least 1 of the given item.
        /// </summary>
        public static bool CellHasRoom(IntVec3 cell, Thing item, Map map)
        {
            if (!cell.InBounds(map)) return false;

            List<Thing> thingsAtCell = cell.GetThingList(map);
            bool hasItem = false;
            foreach (Thing t in thingsAtCell)
            {
                if (t.def.EverStorable(false))
                {
                    if (t.def == item.def && t.stackCount < t.def.stackLimit)
                        return true; // Same type with room
                    if (t.def.EverStorable(false))
                        hasItem = true; // Cell occupied by something
                }
            }
            // Cell is empty of storable items = has room
            return !hasItem;
        }

        /// <summary>
        /// Check if any pawn nearby is actively deconstructing, mining, or harvesting
        /// something that would produce items of the given type. If so, it's worth
        /// waiting for them to finish before hauling.
        /// </summary>
        private static bool IsWorkerProducingNearby(Pawn hauler, ThingDef itemDef, float radius)
        {
            if (hauler.Map == null) return false;

            foreach (Pawn worker in hauler.Map.mapPawns.FreeColonistsSpawned)
            {
                if (worker == hauler) continue;
                if (worker.Dead || worker.Downed) continue;
                if (worker.Position.DistanceTo(hauler.Position) > radius) continue;

                var curJob = worker.CurJob;
                if (curJob == null) continue;

                // Is this worker deconstructing, mining, or harvesting?
                bool isProducing = curJob.def == JobDefOf.Deconstruct
                    || curJob.def == JobDefOf.Mine
                    || curJob.def == JobDefOf.Harvest
                    || curJob.def == JobDefOf.CutPlant;

                if (!isProducing) continue;

                // Check if their target would yield the same item type
                Thing target = curJob.targetA.Thing;
                if (target == null) continue;

                // Deconstruct: check if building yields this material
                if (curJob.def == JobDefOf.Deconstruct && target.def.CostList != null)
                {
                    foreach (var cost in target.def.CostList)
                    {
                        if (cost.thingDef == itemDef)
                            return true;
                    }
                    // Also check stuff type (e.g., steel wall yields steel)
                    if (target.Stuff == itemDef)
                        return true;
                }

                // Mine: check if mineable yields this
                if (curJob.def == JobDefOf.Mine && target.def.building?.mineableThing == itemDef)
                    return true;

                // Harvest/CutPlant: check plant yield
                if ((curJob.def == JobDefOf.Harvest || curJob.def == JobDefOf.CutPlant)
                    && target.def.plant?.harvestedThingDef == itemDef)
                    return true;
            }

            return false;
        }
    }

    // ========== HARMONY PATCHES ==========

    /// <summary>
    /// Patch hauling job creation to use smart hauling for intelligent pawns.
    /// </summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToStorageJob))]
    public static class Patch_HaulToStorageJob
    {
        public static void Postfix(Pawn p, Thing t, ref Job __result)
        {
            try
            {
                if (!LTSSettings.enableHaulingSense) return;
                if (__result == null || t == null || p == null) return;
                if (ModCompat.PUAHLoaded && LTSSettings.respectPUAH) return; // Let PUAH handle it

                var comp = p.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.HaulingSense);
                if (level <= 0) return;

                // Replace vanilla haul job with our smart haul
                if (LTSDefOf.LTS_SmartHaul == null) return;

                // Use the vanilla result's targetB as initial destination
                // (our findCell toil recalculates with room-check anyway)
                IntVec3 storeCell = __result.targetB.Cell;
                if (!storeCell.IsValid)
                {
                    StoreUtility.TryFindBestBetterStoreCellFor(t, p, p.Map,
                        StoreUtility.CurrentStoragePriorityOf(t), p.Faction, out storeCell);
                }
                if (!storeCell.IsValid) return;

                Job smartJob = JobMaker.MakeJob(LTSDefOf.LTS_SmartHaul, t, storeCell);
                smartJob.count = t.stackCount;
                smartJob.haulOpportunisticDuplicates = true;
                __result = smartJob;
            }
            catch (Exception ex)
            {
                LTSLog.Error("HaulToStorageJob patch failed", ex);
            }
        }
    }

    /// <summary>
    /// Award hauling XP when pawns complete vanilla haul jobs (fallback for when our smart haul isn't used).
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_EndJob_HaulingXP
    {
        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            try
            {
                if (!LTSSettings.enableHaulingSense) return;
                if (condition != JobCondition.Succeeded) return;

                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawn == null) return;

                var curJob = __instance.curJob;
                if (curJob == null) return;

                // Only award for vanilla hauls, not our SmartHaul (which awards its own XP)
                if ((curJob.def == JobDefOf.HaulToCell || curJob.def == JobDefOf.HaulToContainer)
                    && (LTSDefOf.LTS_SmartHaul == null || curJob.def != LTSDefOf.LTS_SmartHaul))
                {
                    var comp = pawn.GetComp<CompIntelligence>();
                    comp?.AddXP(StatType.HaulingSense, 6f, "vanilla_haul_complete");
                }
            }
            catch (Exception ex)
            {
                LTSLog.Error("EndJob hauling XP patch failed", ex);
            }
        }
    }

    /// <summary>
    /// Proximity-based hauling preference: intelligent pawns prefer nearby haul targets.
    /// </summary>
    // Manually patched in HarmonyInit - WorkGiver_HaulGeneral.PotentialWorkThingsGlobal
    public static class Patch_HaulWorkThings_Proximity
    {
        public static void Postfix(WorkGiver_HaulGeneral __instance, Pawn pawn, ref IEnumerable<Thing> __result)
        {
            try
            {
                if (!LTSSettings.enableHaulingSense) return;
                if (pawn == null || __result == null) return;

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.HaulingSense);
                float weight = HaulingSense.GetProximityWeight(level);
                if (weight <= 0f) return;

                // Sort by proximity-weighted score
                IntVec3 pawnPos = pawn.Position;
                var items = __result.ToList();

                // Construction priority (Lv13+)
                bool priConstruction = HaulingSense.PrioritizeConstruction(level);
                Map map = pawn.Map;

                items.Sort((a, b) =>
                {
                    float scoreA = a.Position.DistanceTo(pawnPos);
                    float scoreB = b.Position.DistanceTo(pawnPos);

                    // Perishable priority (Lv7+)
                    if (HaulingSense.PrioritizePerishables(level))
                    {
                        if (a.TryGetComp<CompRottable>() != null) scoreA *= 0.5f;
                        if (b.TryGetComp<CompRottable>() != null) scoreB *= 0.5f;
                    }

                    // Construction material priority (Lv13+)
                    if (priConstruction)
                    {
                        if (HaulingSense.IsNeededForConstruction(a, map)) scoreA *= 0.3f;
                        if (HaulingSense.IsNeededForConstruction(b, map)) scoreB *= 0.3f;
                    }

                    return scoreA.CompareTo(scoreB);
                });

                __result = items;

                LTSLog.Decision(pawn, StatType.HaulingSense, level, "PROXIMITY_SORT",
                    items.Count + " candidates",
                    "sorted by proximity (weight=" + weight + ")",
                    "level=" + level);
            }
            catch (Exception ex)
            {
                LTSLog.Error("HaulWorkThings proximity patch failed", ex);
            }
        }
    }
}
