using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    /// <summary>
    /// Job driver for writing a book at a writing spot.
    /// TargetA = writing spot/desk
    /// job.targetQueueB[0] = statType index stored as IntVec3.x (hacky but functional)
    /// </summary>
    public class JobDriver_WriteBook : JobDriver
    {
        private const int WriteDuration = 12000; // ~3.3 hours at 1x speed (200 seconds real time)
        private const TargetIndex SpotInd = TargetIndex.A;

        private StatType StatToWrite
        {
            get
            {
                if (job.bill is Bill_Production bill)
                {
                    // Determine stat from recipe defName
                    if (bill.recipe.defName.Contains("HaulingSense")) return StatType.HaulingSense;
                    if (bill.recipe.defName.Contains("WorkAwareness")) return StatType.WorkAwareness;
                    if (bill.recipe.defName.Contains("PathMemory")) return StatType.PathMemory;
                    if (bill.recipe.defName.Contains("CombatInstinct")) return StatType.CombatInstinct;
                    if (bill.recipe.defName.Contains("SelfPreservation")) return StatType.SelfPreservation;
                }
                return StatType.HaulingSense; // Default
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(SpotInd);
            this.FailOnBurningImmobile(SpotInd);

            // Go to writing spot
            yield return Toils_Goto.GotoThing(SpotInd, PathEndMode.InteractionCell)
                .FailOnSomeonePhysicallyInteracting(SpotInd);

            // Write the book
            Toil writeToil = new Toil();
            writeToil.tickAction = () =>
            {
                pawn.skills?.Learn(SkillDefOf.Intellectual, 0.05f);
            };
            writeToil.defaultCompleteMode = ToilCompleteMode.Delay;
            writeToil.defaultDuration = WriteDuration;
            writeToil.WithProgressBarToilDelay(SpotInd);
            writeToil.FailOnCannotTouch(SpotInd, PathEndMode.InteractionCell);
            writeToil.activeSkill = () => SkillDefOf.Intellectual;
            yield return writeToil;

            // Produce the book
            Toil produceToil = new Toil();
            produceToil.initAction = () =>
            {
                StatType stat = StatToWrite;

                // Check that pawn actually has level 20 in this stat
                var comp = pawn.GetComp<CompIntelligence>();
                if (comp == null || comp.GetLevel(stat) < 20)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Create the book
                Thing book = ThingMaker.MakeThing(LTSDefOf.LTS_Book);
                CompBook bookComp = book.TryGetComp<CompBook>();
                if (bookComp != null)
                {
                    bookComp.statType = stat;
                    bookComp.authorName = pawn.LabelShort;
                    bookComp.authorThingID = pawn.thingIDNumber;
                    bookComp.bookTitle = BookNameGenerator.GenerateTitle(stat, pawn.LabelShort);

                    // Quality based on Intellectual skill
                    int intellect = pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 5;
                    float qualityScore = intellect + Rand.Range(-3f, 3f);
                    if (qualityScore >= 18) bookComp.quality = QualityCategory.Legendary;
                    else if (qualityScore >= 16) bookComp.quality = QualityCategory.Masterwork;
                    else if (qualityScore >= 13) bookComp.quality = QualityCategory.Excellent;
                    else if (qualityScore >= 10) bookComp.quality = QualityCategory.Good;
                    else if (qualityScore >= 6) bookComp.quality = QualityCategory.Normal;
                    else if (qualityScore >= 3) bookComp.quality = QualityCategory.Poor;
                    else bookComp.quality = QualityCategory.Awful;
                }

                // Spawn the book
                GenPlace.TryPlaceThing(book, pawn.Position, pawn.Map, ThingPlaceMode.Near);

                Messages.Message(
                    "LTS_BookWritten".Translate(pawn.LabelShort, book.Label),
                    book, MessageTypeDefOf.PositiveEvent, false);

                LTSLog.Info(pawn.LabelShort + " wrote book: " + (bookComp?.bookTitle ?? "Unknown"));
            };
            produceToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return produceToil;
        }
    }
}
