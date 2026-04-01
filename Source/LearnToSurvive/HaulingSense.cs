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

            float runningMass = primary.GetStatValue(StatDefOf.Mass);
            float massCapacity = useInv ? MassUtility.Capacity(pawn) * 0.9f : float.MaxValue;

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

                float candidateMass = candidate.GetStatValue(StatDefOf.Mass);

                // Inventory mode: limit by carry capacity
                if (useInv)
                {
                    if (runningMass + candidateMass > massCapacity)
                        break;
                }

                result.Add(candidate);
                runningMass += candidateMass;
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
    /// Smart hauling job: picks up items into INVENTORY (multiple types),
    /// walks to nearby items until mass capacity is full, then delivers to storage.
    /// TargetA = first item, TargetB = storage cell, TargetC = current pickup target.
    /// </summary>
    public class JobDriver_SmartHaul : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex DestInd = TargetIndex.B;
        private const TargetIndex NextInd = TargetIndex.C;

        // Storage filter for this batch -- only pick up items accepted by the same stockpile
        private SlotGroup targetSlotGroup;

        // Limit wait-for-production retries to prevent infinite waiting near miners
        private int waitRetries;

        private CompHauledItems HaulComp => pawn.GetComp<CompHauledItems>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        private float MassRemaining()
        {
            return MassUtility.Capacity(pawn) - MassUtility.GearAndInventoryMass(pawn);
        }

        private bool HasRemainingHaulItems()
        {
            var comp = HaulComp;
            return comp != null && comp.HasItems;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Items stay in inventory on interruption -- the CompHauledItems
            // tracks them and they'll be unloaded by the next unload job trigger
            // (idle, haul complete, or rottable check). No emergency dump needed.

            // Fail conditions for initial target only
            this.FailOnDestroyedOrNull(ItemInd);
            this.FailOnBurningImmobile(ItemInd);

            // === 1. Go to the first item (TargetA) ===
            yield return Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(ItemInd);

            // === 2. Pick up first item into INVENTORY ===
            Toil pickupFirst = new Toil();
            pickupFirst.initAction = () =>
            {
                Thing target = job.targetA.Thing;
                if (target == null || target.Destroyed || !target.Spawned)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst target invalid (null={target == null}), ending Incompletable");
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst target={target.LabelShort} x{target.stackCount} at {target.Position}");

                int canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, target);
                if (canTake <= 0)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst canTake=0, too heavy, ending Succeeded");
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                int toTake = UnityEngine.Mathf.Min(canTake, target.stackCount);

                Thing taken = target.SplitOff(toTake);
                if (taken == null)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst SplitOff returned null, ending Incompletable");
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst split off {taken.LabelShort} x{toTake} (canTake={canTake}, stack={target.stackCount})");

                int idBefore = taken.thingIDNumber;
                if (pawn.inventory.innerContainer.TryAdd(taken))
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst TryAdd SUCCESS, registering with comp");
                    HaulComp?.RegisterHauledItem(taken);
                }
                else
                {
                    Log.Warning($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst TryAdd FAILED for {taken.LabelShort}, dropping back");
                    GenPlace.TryPlaceThing(taken, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                // Cache the target storage so we only pick up compatible items
                IntVec3 storageCell;
                if (StoreUtility.TryFindBestBetterStoreCellFor(taken, pawn, pawn.Map,
                    StoragePriority.Unstored, pawn.Faction, out storageCell))
                {
                    targetSlotGroup = storageCell.GetSlotGroup(pawn.Map);
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst cached target stockpile at {storageCell}");
                }
                else
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupFirst no target stockpile found, will accept any compatible items");
                }

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp != null)
                    comp.AddXP(StatType.HaulingSense, 10f, "haul_pickup");
            };
            pickupFirst.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pickupFirst;

            // === Forward-declare toils for jump targets ===
            Toil findCell = new Toil();
            Toil gotoNextItem = new Toil();
            Toil pickupNext = new Toil();
            Toil waitForProduction = new Toil();

            // === 3. Rescan: find nearest haulable item from current position ===
            Toil rescan = new Toil();
            rescan.initAction = () =>
            {
                float massLeft = MassRemaining();
                if (massLeft <= 0.01f)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: rescan massLeft={massLeft:F2}, full -> findCell");
                    JumpToToil(findCell);
                    return;
                }

                var comp = pawn.GetComp<CompIntelligence>();
                int level = comp?.GetLevel(StatType.HaulingSense) ?? 0;
                float radius = HaulingSense.GetGrabRadius(level);

                Thing bestNext = null;
                float bestDist = float.MaxValue;
                int candidateCount = 0;

                foreach (Thing nearby in GenRadial.RadialDistinctThingsAround(
                    pawn.Position, pawn.Map, radius, true))
                {
                    if (!nearby.def.EverHaulable) continue;
                    if (nearby.Destroyed || !nearby.Spawned) continue;
                    if (nearby.IsForbidden(pawn)) continue;
                    if (nearby.IsInValidBestStorage()) continue;
                    if (!pawn.CanReserve(nearby)) continue;
                    // Skip items that shouldn't go in inventory (vanilla haul handles them fine)
                    if (nearby.def.IsWeapon || nearby.def.IsApparel) continue;
                    if (nearby.def.IsMedicine) continue;
                    if (nearby is Corpse) continue;
                    if (nearby.def.minifiedDef != null) continue;
                    if (nearby.TryGetComp<CompBook>() != null) continue; // Books get lost in inventory
                    if (nearby.GetStatValue(StatDefOf.Mass) > 8f) continue; // Chunks/heavy items: vanilla carry

                    // Only pick up items compatible with our target stockpile
                    if (targetSlotGroup != null && !targetSlotGroup.Settings.AllowedToAccept(nearby))
                        continue;

                    // Check mass: can we pick up at least 1?
                    int canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, nearby);
                    if (canTake <= 0) continue;

                    candidateCount++;
                    float dist = nearby.Position.DistanceTo(pawn.Position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestNext = nearby;
                    }
                }

                Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: rescan massLeft={massLeft:F2}, radius={radius}, candidates={candidateCount}, best={bestNext?.LabelShort ?? "none"}{(bestNext != null ? $" x{bestNext.stackCount} at {bestNext.Position} dist={bestDist:F1}" : "")}");

                if (bestNext != null)
                {
                    if (!pawn.Reserve(bestNext, job, 1, -1, null, false))
                    {
                        Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: rescan reserve FAILED for {bestNext.LabelShort}, re-scanning");
                        JumpToToil(rescan);
                        return;
                    }
                    if (job.targetC.Thing != null && job.targetC.Thing.Spawned)
                        pawn.Map.reservationManager.Release(job.targetC, pawn, job);
                    job.targetC = new LocalTargetInfo(bestNext);
                    JumpToToil(gotoNextItem);
                    return;
                }

                // Nothing found. Check if a worker nearby is producing items.
                bool workerNearby = false;
                foreach (Pawn worker in pawn.Map.mapPawns.FreeColonistsSpawned)
                {
                    if (worker == pawn) continue;
                    if (worker.Dead || worker.Downed) continue;
                    if (worker.Position.DistanceTo(pawn.Position) > radius) continue;
                    var curJob = worker.CurJob;
                    if (curJob == null) continue;
                    if (curJob.def == JobDefOf.Deconstruct || curJob.def == JobDefOf.Mine
                        || curJob.def == JobDefOf.Harvest || curJob.def == JobDefOf.CutPlant)
                    {
                        workerNearby = true;
                        break;
                    }
                }

                if (workerNearby && waitRetries < 3)
                {
                    waitRetries++;
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: rescan worker nearby, waiting (retry {waitRetries}/3)");
                    JumpToToil(waitForProduction);
                    return;
                }

                Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: rescan nothing found, going to deliver");
                JumpToToil(findCell);
            };
            rescan.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return rescan;

            // === 4. Walk to next item (TargetC) ===
            gotoNextItem.initAction = () =>
            {
                Thing target = job.targetC.Thing;
                if (target == null || target.Destroyed || !target.Spawned)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: gotoNextItem target invalid, re-scanning");
                    JumpToToil(rescan);
                }
                else
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: gotoNextItem walking to {target.LabelShort} x{target.stackCount} at {target.Position}");
                    pawn.pather.StartPath(target, PathEndMode.ClosestTouch);
                }
            };
            gotoNextItem.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            gotoNextItem.AddFailCondition(() =>
            {
                Thing t = job.targetC.Thing;
                return t == null || t.Destroyed;
            });
            yield return gotoNextItem;

            // === 5. Pick up item into inventory ===
            pickupNext.initAction = () =>
            {
                Thing target = job.targetC.Thing;
                if (target == null || target.Destroyed || !target.Spawned)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupNext target invalid, re-scanning");
                    JumpToToil(rescan);
                    return;
                }

                int canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, target);
                if (canTake <= 0)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupNext canTake=0 for {target.LabelShort}, full -> findCell");
                    JumpToToil(findCell);
                    return;
                }
                int toTake = UnityEngine.Mathf.Min(canTake, target.stackCount);

                Thing taken = target.SplitOff(toTake);
                if (taken == null)
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupNext SplitOff returned null for {target.LabelShort}, re-scanning");
                    JumpToToil(rescan);
                    return;
                }

                if (pawn.inventory.innerContainer.TryAdd(taken))
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickupNext SUCCESS {taken.LabelShort} x{toTake} (canTake={canTake}, stack={target.stackCount})");
                    HaulComp?.RegisterHauledItem(taken);
                }
                else
                {
                    Log.Warning($"[LTS-SmartHaul] {pawn.LabelShort}: pickupNext TryAdd FAILED for {taken.LabelShort}, dropping back -> findCell");
                    GenPlace.TryPlaceThing(taken, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    JumpToToil(findCell);
                    return;
                }

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp != null)
                    comp.AddXP(StatType.HaulingSense, 3f, "inv_pickup");
            };
            pickupNext.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pickupNext;

            // === 6. Loop back to rescan ===
            Toil loopBack = new Toil();
            loopBack.initAction = () =>
            {
                JumpToToil(rescan);
            };
            loopBack.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loopBack;

            // === 7. Wait for production (~3 seconds), then rescan ===
            waitForProduction.initAction = () =>
            {
                Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: waitForProduction entering wait (retry {waitRetries}/3)");
            };
            waitForProduction.defaultCompleteMode = ToilCompleteMode.Delay;
            waitForProduction.defaultDuration = 180;
            yield return waitForProduction;

            Toil postWait = new Toil();
            postWait.initAction = () =>
            {
                Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: waitForProduction done, re-scanning");
                JumpToToil(rescan);
            };
            postWait.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return postWait;

            // === 8. Queue separate unload job and end ===
            // Delivery MUST be a separate job because SmartHaul's FailOnDestroyedOrNull(ItemInd)
            // checks every tick across ALL toils. When PlaceHauledThingInCell places an item
            // that merges with an existing stack, the original Thing is destroyed, FailOn fires,
            // and kills the job mid-delivery. This is why PUAH uses a separate unload job.
            findCell.initAction = () =>
            {
                var hComp = HaulComp;
                int itemCount = hComp?.Count ?? 0;
                Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: pickup phase done, {itemCount} items in comp, inventory={pawn.inventory.innerContainer.Count}");

                if (HasRemainingHaulItems())
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: queuing LTS_UnloadHauledItems");
                    Job unloadJob = JobMaker.MakeJob(LTSDefOf.LTS_UnloadHauledItems);
                    pawn.jobs.jobQueue.EnqueueFirst(unloadJob, null);
                }
                else
                {
                    Log.Message($"[LTS-SmartHaul] {pawn.LabelShort}: no remaining haul items, ending");
                }
            };
            findCell.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findCell;
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
        public static bool Prepare() => LTSSettings.enableHaulingSense;

        public static void Postfix(Pawn p, Thing t, ref Job __result)
        {
            try
            {
                if (__result == null || t == null || p == null) return;
                if (ModCompat.PUAHLoaded && LTSSettings.respectPUAH)
                {
                    Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, PUAH active");
                    return;
                }
                // Don't use inventory hauling for items that shouldn't go in inventory
                // These get hauled normally via vanilla carry (one at a time, straight to storage)
                if (t.def.IsWeapon || t.def.IsApparel) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, weapon/apparel"); return; }
                if (t.def.IsMedicine) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, medicine"); return; }
                if (t is Corpse) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, corpse"); return; }
                if (t.def.minifiedDef != null) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, minified"); return; }
                if (t.TryGetComp<CompBook>() != null) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, book"); return; }
                if (t.GetStatValue(StatDefOf.Mass) > 8f) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, too heavy ({t.GetStatValue(StatDefOf.Mass):F1}kg)"); return; }
                if (!MassUtility.CanEverCarryAnything(p)) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, pawn can't carry"); return; }

                var comp = p.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.HaulingSense);
                if (level <= 0) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, hauling level=0"); return; }

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
                if (!storeCell.IsValid) { Log.Message($"[LTS-HaulPatch] {p.LabelShort}: skip {t.LabelShort}, no valid storage cell"); return; }

                Job smartJob = JobMaker.MakeJob(LTSDefOf.LTS_SmartHaul, t, storeCell);
                smartJob.count = t.stackCount;
                smartJob.haulOpportunisticDuplicates = true;
                __result = smartJob;
                Log.Message($"[LTS-HaulPatch] {p.LabelShort}: replaced vanilla haul with SmartHaul for {t.LabelShort} x{t.stackCount} -> {storeCell} (level={level})");
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
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> jobTrackerPawn =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        public static bool Prepare() => LTSSettings.enableHaulingSense;

        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            try
            {
                if (!LTSSettings.enableHaulingSense) return;
                if (condition != JobCondition.Succeeded) return;

                Pawn pawn = jobTrackerPawn(__instance);
                if (pawn == null || !pawn.IsColonistPlayerControlled) return;

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
    /// Global inventory return on job interruption:
    /// When a pawn is carrying something from our smart haul system and gets interrupted,
    /// put it back in inventory instead of dropping on the ground.
    /// Pattern: Prefix saves what's being carried, Postfix picks it up from ground after vanilla drops it.
    /// This avoids fighting with vanilla's CleanupCurrentJob which always calls TryDropCarriedThing.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
    public static class Patch_CleanupJob_InventoryReturn
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> jobTrackerPawn =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        // ThreadStatic: prefix saves carried thing ID, postfix retrieves it
        [ThreadStatic] private static int savedThingId;
        [ThreadStatic] private static bool shouldRecover;

        public static bool Prepare() => LTSSettings.enableHaulingSense;

        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            shouldRecover = false;
            try
            {
                if (condition == JobCondition.Succeeded) return;

                Pawn pawn = jobTrackerPawn(__instance);
                if (pawn == null || pawn.Map == null) return;
                if (!pawn.IsColonistPlayerControlled) return;

                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried == null) return;

                var haulComp = pawn.GetComp<CompHauledItems>();
                if (haulComp == null || !haulComp.IsTracked(carried)) return;

                // Item is tracked by our haul system
                {
                    Log.Message($"[LTS-InvReturn] {pawn.LabelShort}: saving carried item {carried.LabelShort} x{carried.stackCount} (id={carried.thingIDNumber}) for recovery after cleanup (condition={condition})");
                    savedThingId = carried.thingIDNumber;
                    shouldRecover = true;
                }
            }
            catch (Exception) { }
        }

        public static void Postfix(Pawn_JobTracker __instance)
        {
            try
            {
                if (!shouldRecover) return;
                shouldRecover = false;

                Pawn pawn = jobTrackerPawn(__instance);
                if (pawn == null || pawn.Map == null) return;

                Log.Message($"[LTS-InvReturn] {pawn.LabelShort}: searching for dropped item id={savedThingId} near {pawn.Position}");

                // Vanilla already dropped it. Find it near the pawn and move to inventory.
                foreach (Thing t in pawn.Position.GetThingList(pawn.Map))
                {
                    if (t.thingIDNumber == savedThingId && t.Spawned)
                    {
                        if (t.def.EverHaulable && pawn.inventory.innerContainer.TryAdd(t))
                        {
                            Log.Message($"[LTS-InvReturn] {pawn.LabelShort}: recovered {t.LabelShort} x{t.stackCount} from pawn cell {pawn.Position}");
                            var comp = pawn.GetComp<CompIntelligence>();
                            LTSLog.Decision(pawn, StatType.HaulingSense,
                                comp?.GetLevel(StatType.HaulingSense) ?? 0, "INV_RETURN",
                                "recovered " + t.LabelShort + " from ground",
                                "returned to inventory after job interruption",
                                "preventing ground drop");
                        }
                        else
                        {
                            Log.Warning($"[LTS-InvReturn] {pawn.LabelShort}: found item id={savedThingId} but TryAdd failed");
                        }
                        return;
                    }
                }

                // Check adjacent cells too (TryDropCarriedThing uses ThingPlaceMode.Near)
                foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(pawn))
                {
                    if (!cell.InBounds(pawn.Map)) continue;
                    foreach (Thing t in cell.GetThingList(pawn.Map))
                    {
                        if (t.thingIDNumber == savedThingId && t.Spawned)
                        {
                            if (t.def.EverHaulable && pawn.inventory.innerContainer.TryAdd(t))
                            {
                                Log.Message($"[LTS-InvReturn] {pawn.LabelShort}: recovered {t.LabelShort} x{t.stackCount} from adjacent cell {cell}");
                                var comp = pawn.GetComp<CompIntelligence>();
                                LTSLog.Decision(pawn, StatType.HaulingSense,
                                    comp?.GetLevel(StatType.HaulingSense) ?? 0, "INV_RETURN",
                                    "recovered " + t.LabelShort + " from adjacent cell",
                                    "returned to inventory after job interruption",
                                    "preventing ground drop");
                            }
                            else
                            {
                                Log.Warning($"[LTS-InvReturn] {pawn.LabelShort}: found item id={savedThingId} at {cell} but TryAdd failed");
                            }
                            return;
                        }
                    }
                }
                Log.Warning($"[LTS-InvReturn] {pawn.LabelShort}: could NOT find dropped item id={savedThingId} anywhere near pawn");
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Unload trigger: when pawn goes idle and has hauled items, start unloading.
    /// Pattern from PUAH.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Idle), "TryGiveJob")]
    public static class Patch_IdleUnloadTrigger
    {
        public static bool Prepare() => LTSSettings.enableHaulingSense;

        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (pawn == null || !pawn.IsColonistPlayerControlled) return;

                // Don't trigger if already doing or queued for unload
                if (pawn.CurJobDef == LTSDefOf.LTS_UnloadHauledItems) return;

                var comp = pawn.GetComp<CompHauledItems>();
                if (comp == null || !comp.HasItems) return;

                // Verify items are actually still in inventory (not just stale references)
                Thing firstItem = comp.FirstUnloadableThing(pawn);
                if (firstItem == null) return;

                Log.Message($"[LTS-IdleUnload] {pawn.LabelShort}: idle with {comp.Count} hauled items, triggering unload (first={firstItem.LabelShort} x{firstItem.stackCount})");
                // Force unload when idle
                __result = JobMaker.MakeJob(LTSDefOf.LTS_UnloadHauledItems);
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Unload trigger: when pawn completes a normal haul-to-cell and has hauled items,
    /// queue an unload job. Also triggers if perishables are about to rot.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_HaulCompleteUnloadTrigger
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> jobTrackerPawn =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        public static bool Prepare() => LTSSettings.enableHaulingSense;

        [HarmonyPriority(Priority.Low)] // Run after other EndJob patches
        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            try
            {
                if (condition != JobCondition.Succeeded) return;

                Pawn pawn = jobTrackerPawn(__instance);
                if (pawn == null || pawn.Map == null || !pawn.IsColonistPlayerControlled) return;

                var comp = pawn.GetComp<CompHauledItems>();
                if (comp == null || !comp.HasItems) return;

                var curJob = __instance.curJob;
                if (curJob == null) return;

                // Don't re-trigger if current job is already an unload or our smart haul
                if (curJob.def == LTSDefOf.LTS_UnloadHauledItems) return;
                if (curJob.def == LTSDefOf.LTS_SmartHaul) return;

                // Trigger unload after normal haul, or if perishables are about to rot
                bool shouldUnload = false;
                string reason = null;
                if (curJob.def == JobDefOf.HaulToCell || curJob.def == JobDefOf.HaulToContainer)
                {
                    shouldUnload = true;
                    reason = "haul complete";
                }
                if (comp.HasPerishableSoon(pawn))
                {
                    shouldUnload = true;
                    reason = "perishable about to rot";
                }

                if (shouldUnload)
                {
                    Log.Message($"[LTS-HaulUnload] {pawn.LabelShort}: triggering unload after job {curJob.def.defName}, reason={reason}, tracked={comp.Count}");
                    Job unloadJob = JobMaker.MakeJob(LTSDefOf.LTS_UnloadHauledItems);
                    __instance.jobQueue.EnqueueFirst(unloadJob, null);
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Drop protection: prevent vanilla from dropping our hauled items as "unused inventory".
    /// Without this, JobGiver_DropUnusedInventory will periodically drop hauled items.
    /// Pattern from PUAH.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "Drop")]
    public static class Patch_PreventDropHauledItems
    {
        public static bool Prepare() => LTSSettings.enableHaulingSense;

        public static bool Prefix(Pawn pawn, Thing thing)
        {
            try
            {
                var comp = pawn.GetComp<CompHauledItems>();
                if (comp != null && comp.IsTracked(thing))
                {
                    Log.Message($"[LTS-DropProtect] {pawn.LabelShort}: preventing vanilla drop of tracked item {thing.LabelShort} x{thing.stackCount}");
                    return false; // Don't drop -- it's a hauled item
                }
            }
            catch (Exception) { }
            return true;
        }
    }

    /// <summary>
    /// Inventory sync: when any item is removed from inventory by any means,
    /// remove it from our tracking. Keeps CompHauledItems in sync automatically.
    /// Pattern from PUAH.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.Notify_ItemRemoved))]
    public static class Patch_InventorySync
    {
        public static bool Prepare() => LTSSettings.enableHaulingSense;

        public static void Postfix(Pawn_InventoryTracker __instance, Thing item)
        {
            try
            {
                Pawn pawn = __instance.pawn;
                if (pawn == null) return;

                var comp = pawn.GetComp<CompHauledItems>();
                if (comp != null && comp.IsTracked(item))
                {
                    Log.Message($"[LTS-InvSync] {pawn.LabelShort}: auto-removing tracked item {item?.LabelShort ?? "NULL"} (removed from inventory)");
                    comp.UnregisterHauledItem(item);
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Remove vanilla pickup limit for our haul system.
    /// Vanilla PawnUtility.GetMaxAllowedToPickUp limits stack pickup counts.
    /// Pattern from PUAH.
    /// </summary>
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GetMaxAllowedToPickUp),
        new Type[] { typeof(Pawn), typeof(ThingDef) })]
    public static class Patch_RemovePickupLimit
    {
        public static bool Prepare() => LTSSettings.enableHaulingSense;

        public static void Postfix(Pawn pawn, ref int __result)
        {
            try
            {
                // Only boost limit when our system is active (pawn has smart haul capability)
                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null) return;

                int level = comp.GetLevel(StatType.HaulingSense);
                if (HaulingSense.UseInventory(level)) // Lv4+
                {
                    Log.Message($"[LTS-PickupLimit] {pawn.LabelShort}: boosting pickup limit from {__result} to MaxValue (level={level})");
                    __result = int.MaxValue;
                }
            }
            catch (Exception) { }
        }
    }

}
