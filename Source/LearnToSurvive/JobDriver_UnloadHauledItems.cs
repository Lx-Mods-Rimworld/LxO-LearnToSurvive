using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    /// <summary>
    /// Unload hauled items from inventory to storage, one at a time.
    /// Matches PUAH's UnloadYourHauledInventory pattern exactly:
    /// - Separate toils for find, pull, walk, place
    /// - NO FailOnDestroyedOrNull (avoids stack-merge kills)
    /// - Uses vanilla TryTransferToContainer + PlaceHauledThingInCell
    /// </summary>
    public class JobDriver_UnloadHauledItems : JobDriver
    {
        private int countToDrop = -1;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Reservations are made per-item in the find toil
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref countToDrop, "countToDrop", -1);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // NO FailOnDestroyedOrNull -- we manage item lifecycle ourselves.
            // This is critical: vanilla PlaceHauledThingInCell can merge stacks,
            // destroying the carried Thing. A FailOn would kill the job.

            // === 1. Wait briefly between deliveries ===
            Toil wait = Toils_General.Wait(3);
            yield return wait;

            // === 2. Find next unloadable item and reserve its storage destination ===
            Toil findAndReserve = new Toil();
            findAndReserve.initAction = () =>
            {
                CompHauledItems comp = pawn.GetComp<CompHauledItems>();
                if (comp == null || !comp.HasItems)
                {
                    Log.Message($"[LTS-Unload] {pawn.LabelShort}: no items left, ending job");
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                Thing unloadable = comp.FirstUnloadableThing(pawn);
                if (unloadable == null)
                {
                    Log.Message($"[LTS-Unload] {pawn.LabelShort}: FirstUnloadableThing returned null (comp has {comp.Count} tracked), ending");
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                Log.Message($"[LTS-Unload] {pawn.LabelShort}: unloading {unloadable.LabelShort} x{unloadable.stackCount}");

                IntVec3 storeCell;
                if (!StoreUtility.TryFindBestBetterStoreCellFor(unloadable, pawn, pawn.Map,
                    StoragePriority.Unstored, pawn.Faction, out storeCell))
                {
                    Log.Warning($"[LTS-Unload] {pawn.LabelShort}: no storage for {unloadable.LabelShort}, dropping near pawn");
                    Thing dropped;
                    pawn.inventory.innerContainer.TryDrop(unloadable, pawn.Position, pawn.Map,
                        ThingPlaceMode.Near, out dropped);
                    if (dropped != null) dropped.SetForbidden(false, false);
                    comp.UnregisterHauledItem(unloadable);

                    if (comp.HasItems) { JumpToToil(wait); return; }
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                if (!pawn.Reserve(storeCell, job))
                {
                    Log.Warning($"[LTS-Unload] {pawn.LabelShort}: can't reserve {storeCell} for {unloadable.LabelShort}, dropping");
                    Thing dropped;
                    pawn.inventory.innerContainer.TryDrop(unloadable, pawn.Position, pawn.Map,
                        ThingPlaceMode.Near, out dropped);
                    if (dropped != null) dropped.SetForbidden(false, false);
                    comp.UnregisterHauledItem(unloadable);

                    if (comp.HasItems) { JumpToToil(wait); return; }
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                Log.Message($"[LTS-Unload] {pawn.LabelShort}: reserved {storeCell}, setting targets");
                job.targetA = unloadable;
                job.targetB = storeCell;
                countToDrop = unloadable.stackCount;
            };
            findAndReserve.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findAndReserve;

            // === 3. Pull item from inventory into carry slot ===
            Toil pullFromInventory = new Toil();
            pullFromInventory.initAction = () =>
            {
                Thing unloadable = job.targetA.Thing;
                if (unloadable == null)
                {
                    Log.Warning($"[LTS-Unload] {pawn.LabelShort}: targetA.Thing is null at pull, skipping");
                    CompHauledItems comp = pawn.GetComp<CompHauledItems>();
                    pawn.Map?.reservationManager.Release(job.targetB.Cell, pawn, job);
                    if (comp != null && comp.HasItems) { JumpToToil(wait); return; }
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                Log.Message($"[LTS-Unload] {pawn.LabelShort}: transferring {unloadable.LabelShort} x{countToDrop} to carry slot");

                Thing carried;
                int transferred = pawn.inventory.innerContainer.TryTransferToContainer(
                    unloadable, pawn.carryTracker.innerContainer, countToDrop, out carried);

                Log.Message($"[LTS-Unload] {pawn.LabelShort}: transfer count={transferred}, carried={carried?.LabelShort ?? "NULL"}, carryThing={pawn.carryTracker.CarriedThing?.LabelShort ?? "NULL"}");

                CompHauledItems hComp = pawn.GetComp<CompHauledItems>();
                hComp?.UnregisterHauledItem(unloadable);

                if (carried == null)
                {
                    Log.Warning($"[LTS-Unload] {pawn.LabelShort}: transfer FAILED for {unloadable.LabelShort}, releasing reservation");
                    pawn.Map?.reservationManager.Release(job.targetB.Cell, pawn, job);
                    if (hComp != null && hComp.HasItems) { JumpToToil(wait); return; }
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                carried.SetForbidden(false, false);
                Log.Message($"[LTS-Unload] {pawn.LabelShort}: now carrying {pawn.carryTracker.CarriedThing?.LabelShort ?? "NULL"}, walking to {job.targetB.Cell}");
            };
            pullFromInventory.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pullFromInventory;

            // === 4. Walk to storage cell ===
            Toil gotoCell = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.ClosestTouch);
            yield return gotoCell;

            // === 5. Place carried thing in cell (vanilla placement) ===
            yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, gotoCell, true);

            // === 6. Release reservation and loop ===
            Toil releaseAndLoop = new Toil();
            releaseAndLoop.initAction = () =>
            {
                Log.Message($"[LTS-Unload] {pawn.LabelShort}: placed item at {job.targetB.Cell}");
                // PlaceHauledThingInCell already releases the reservation -- don't double-release

                CompHauledItems comp = pawn.GetComp<CompHauledItems>();
                if (comp != null && comp.HasItems)
                {
                    Log.Message($"[LTS-Unload] {pawn.LabelShort}: {comp.Count} items remaining, looping");
                    JumpToToil(wait);
                    return;
                }

                Log.Message($"[LTS-Unload] {pawn.LabelShort}: all items delivered");
                var intel = pawn.GetComp<CompIntelligence>();
                intel?.AddXP(StatType.HaulingSense, 5f, "haul_deliver");
                EndJobWith(JobCondition.Succeeded);
            };
            releaseAndLoop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return releaseAndLoop;
        }
    }
}
