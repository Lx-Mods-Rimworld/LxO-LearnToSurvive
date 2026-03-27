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
    /// Smart hauling job: picks up items into INVENTORY (multiple types),
    /// walks to nearby items until mass capacity is full, then delivers to storage.
    /// TargetA = first item, TargetB = storage cell, TargetC = current pickup target.
    /// </summary>
    public class JobDriver_SmartHaul : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex DestInd = TargetIndex.B;
        private const TargetIndex NextInd = TargetIndex.C;

        // Track which inventory items are haul-items vs personal gear
        private HashSet<int> haulItemIDs = new HashSet<int>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        private float MassRemaining()
        {
            return MassUtility.Capacity(pawn) - MassUtility.GearAndInventoryMass(pawn);
        }

        /// <summary>
        /// Drop all tracked haul items from inventory onto the ground near the pawn.
        /// </summary>
        private void UnloadHaulItems()
        {
            try
            {
                if (pawn.Map == null) return;
                var inv = pawn.inventory.innerContainer;
                // Iterate backwards to safely remove during iteration
                for (int i = inv.Count - 1; i >= 0; i--)
                {
                    Thing item = inv[i];
                    if (!haulItemIDs.Contains(item.thingIDNumber)) continue;

                    Thing dropped;
                    if (inv.TryDrop(item, pawn.Position, pawn.Map, ThingPlaceMode.Near, out dropped))
                    {
                        if (dropped != null && dropped.Spawned)
                            dropped.SetForbidden(false, false);
                    }
                }
                haulItemIDs.Clear();
            }
            catch (Exception ex)
            {
                LTSLog.Error("UnloadHaulItems failed", ex);
                haulItemIDs.Clear();
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Safety: if job is interrupted for ANY reason, drop all haul items
            // back on the ground so they don't stay in inventory forever
            this.AddFinishAction((JobCondition _) =>
            {
                if (haulItemIDs.Count > 0)
                    UnloadHaulItems();
            });

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
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, target);
                if (canTake <= 0)
                {
                    // Can't carry -- end gracefully (Succeeded prevents retry loop)
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                int toTake = UnityEngine.Mathf.Min(canTake, target.stackCount);

                Thing taken = target.SplitOff(toTake);
                if (taken == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int idBefore = taken.thingIDNumber;
                if (pawn.inventory.innerContainer.TryAdd(taken))
                {
                    haulItemIDs.Add(taken.thingIDNumber);
                    // If the item merged into an existing stack, track original ID too
                    if (taken.thingIDNumber != idBefore)
                        haulItemIDs.Add(idBefore);
                }
                else
                {
                    // Failed to add to inventory -- drop it back, end gracefully
                    GenPlace.TryPlaceThing(taken, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                    EndJobWith(JobCondition.Succeeded); // Succeeded prevents retry loop
                    return;
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
                    JumpToToil(findCell);
                    return;
                }

                var comp = pawn.GetComp<CompIntelligence>();
                int level = comp?.GetLevel(StatType.HaulingSense) ?? 0;
                float radius = HaulingSense.GetGrabRadius(level);

                Thing bestNext = null;
                float bestDist = float.MaxValue;

                foreach (Thing nearby in GenRadial.RadialDistinctThingsAround(
                    pawn.Position, pawn.Map, radius, true))
                {
                    if (!nearby.def.EverHaulable) continue;
                    if (nearby.Destroyed || !nearby.Spawned) continue;
                    if (nearby.IsForbidden(pawn)) continue;
                    if (nearby.IsInValidBestStorage()) continue;
                    if (!pawn.CanReserve(nearby)) continue;
                    // Skip items that can't go in inventory
                    if (nearby.def.IsWeapon || nearby.def.IsApparel) continue;
                    if (nearby is Corpse) continue;
                    if (nearby.def.minifiedDef != null) continue;

                    // Check mass: can we pick up at least 1?
                    int canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, nearby);
                    if (canTake <= 0) continue;

                    float dist = nearby.Position.DistanceTo(pawn.Position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestNext = nearby;
                    }
                }

                if (bestNext != null)
                {
                    if (!pawn.Reserve(bestNext, job, 1, -1, null, false))
                    {
                        // Item grabbed by someone else between scan and reserve -- re-scan
                        JumpToToil(rescan);
                        return;
                    }
                    if (job.targetC.Thing != null && job.targetC.Thing.Spawned)
                        pawn.Map.reservationManager.Release(job.targetC, pawn, job);
                    // TODO: Items of different types may end up in wrong storage. Need per-type destination finding.
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

                if (workerNearby)
                {
                    JumpToToil(waitForProduction);
                    return;
                }

                // Nothing to pick up, go deliver
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
                    JumpToToil(rescan);
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
            yield return gotoNextItem;

            // === 5. Pick up item into inventory ===
            pickupNext.initAction = () =>
            {
                Thing target = job.targetC.Thing;
                if (target == null || target.Destroyed || !target.Spawned)
                {
                    JumpToToil(rescan);
                    return;
                }

                int canTake = MassUtility.CountToPickUpUntilOverEncumbered(pawn, target);
                if (canTake <= 0)
                {
                    JumpToToil(findCell);
                    return;
                }
                int toTake = UnityEngine.Mathf.Min(canTake, target.stackCount);

                Thing taken = target.SplitOff(toTake);
                if (taken == null)
                {
                    JumpToToil(rescan);
                    return;
                }

                int idBefore = taken.thingIDNumber;
                if (pawn.inventory.innerContainer.TryAdd(taken))
                {
                    haulItemIDs.Add(taken.thingIDNumber);
                    if (taken.thingIDNumber != idBefore)
                        haulItemIDs.Add(idBefore);
                }
                else
                {
                    GenPlace.TryPlaceThing(taken, pawn.Position, pawn.Map, ThingPlaceMode.Near);
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
            waitForProduction.defaultCompleteMode = ToilCompleteMode.Delay;
            waitForProduction.defaultDuration = 180;
            yield return waitForProduction;

            Toil postWait = new Toil();
            postWait.initAction = () =>
            {
                JumpToToil(rescan);
            };
            postWait.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return postWait;

            // === 8. Find storage cell ===
            findCell.initAction = () =>
            {
                // Need a representative item to find storage for.
                // Pick the first haul item in inventory.
                Thing representative = null;
                var inv = pawn.inventory.innerContainer;
                for (int i = 0; i < inv.Count; i++)
                {
                    if (haulItemIDs.Contains(inv[i].thingIDNumber))
                    {
                        representative = inv[i];
                        break;
                    }
                }

                if (representative == null)
                {
                    // No haul items in inventory -- nothing to deliver
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                IntVec3 storeCell = FindCellWithRoom(representative, pawn);
                if (!storeCell.IsValid)
                {
                    // No valid storage -- dump everything here
                    UnloadHaulItems();
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                job.targetB = storeCell;
            };
            findCell.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findCell;

            // === 9. Walk to storage ===
            yield return Toils_Goto.GotoCell(DestInd, PathEndMode.OnCell);

            // === 10. Unload all haul-items from inventory ===
            Toil unloadToil = new Toil();
            unloadToil.initAction = () =>
            {
                IntVec3 destCell = job.targetB.Cell;
                var inv = pawn.inventory.innerContainer;

                for (int i = inv.Count - 1; i >= 0; i--)
                {
                    Thing item = inv[i];
                    if (!haulItemIDs.Contains(item.thingIDNumber)) continue;

                    Thing dropped;
                    if (inv.TryDrop(item, destCell, pawn.Map, ThingPlaceMode.Near, out dropped))
                    {
                        if (dropped != null && dropped.Spawned)
                            dropped.SetForbidden(false, false);
                    }
                }
                haulItemIDs.Clear();

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp != null)
                    comp.AddXP(StatType.HaulingSense, 5f, "haul_deliver");
            };
            unloadToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return unloadToil;
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
                if (ModCompat.PUAHLoaded && LTSSettings.respectPUAH) return;
                // Don't use inventory hauling for items that can't go in inventory
                if (t.def.IsWeapon || t.def.IsApparel) return;
                if (t is Corpse) return;
                if (t.def.minifiedDef != null) return;
                if (!MassUtility.CanEverCarryAnything(p)) return;

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

}
