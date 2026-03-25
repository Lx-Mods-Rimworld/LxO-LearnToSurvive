using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    /// <summary>
    /// Job driver for reading a book. Gives joy and intelligence XP.
    /// TargetA = the book thing
    /// </summary>
    public class JobDriver_ReadBook : JobDriver
    {
        private const int ReadDuration = 4000; // ~67 seconds at 1x (~1.1 in-game hours)
        private const TargetIndex BookInd = TargetIndex.A;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(BookInd);

            // Go to the book
            yield return Toils_Goto.GotoThing(BookInd, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(BookInd);

            // Pick up the book
            Toil pickUp = new Toil();
            pickUp.initAction = () =>
            {
                Thing book = job.targetA.Thing;
                if (book == null || book.Destroyed)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                pawn.carryTracker.TryStartCarry(book);
            };
            pickUp.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pickUp;

            // Find a comfortable spot to read (preferably a chair/table area)
            Toil findSeat = new Toil();
            findSeat.initAction = () =>
            {
                // Try to find a chair or comfortable spot
                IntVec3 readSpot = pawn.Position;

                // Look for nearby chairs
                Thing chair = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                    PathEndMode.OnCell, TraverseParms.For(pawn), 20f,
                    t => t.def.building != null && t.def.building.isSittable
                        && !t.IsForbidden(pawn) && pawn.CanReserve(t));

                if (chair != null)
                {
                    pawn.Reserve(chair, job);
                    job.targetB = chair;
                }
                else
                {
                    job.targetB = pawn.Position;
                }
            };
            findSeat.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findSeat;

            // Go to the reading spot
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            // Read the book (gives joy + XP)
            Toil readToil = new Toil();
            readToil.tickAction = () =>
            {
                // Joy gain
                pawn.needs?.joy?.GainJoy(0.00036f, LTSDefOf.LTS_Reading);
            };
            readToil.defaultCompleteMode = ToilCompleteMode.Delay;
            readToil.defaultDuration = ReadDuration;
            readToil.WithProgressBar(BookInd, () =>
            {
                float elapsed = Find.TickManager.TicksGame - startTick;
                return elapsed / ReadDuration;
            });
            readToil.handlingFacing = true;
            yield return readToil;

            // Finish reading: award XP
            Toil finishToil = new Toil();
            finishToil.initAction = () =>
            {
                Thing bookThing = pawn.carryTracker.CarriedThing;
                if (bookThing == null)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                CompBook bookComp = bookThing.TryGetComp<CompBook>();
                if (bookComp == null)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                var comp = pawn.GetComp<CompIntelligence>();
                if (comp != null)
                {
                    float xp = bookComp.GetXPForReader(pawn);
                    comp.AddXP(bookComp.statType, xp, "read_book:" + bookComp.bookTitle);
                    comp.RecordBookRead(bookThing.thingIDNumber);

                    LTSLog.Decision(pawn, bookComp.statType, comp.GetLevel(bookComp.statType),
                        "BOOK_READ",
                        "book=" + bookComp.bookTitle + " quality=" + bookComp.quality,
                        "gained " + xp.ToString("F1") + " XP in " + bookComp.statType,
                        "readCount=" + comp.GetBookReadCount(bookThing.thingIDNumber));
                }

                // Drop the book
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing dropped);
            };
            finishToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finishToil;
        }
    }
}
