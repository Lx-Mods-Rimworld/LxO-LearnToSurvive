using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace LearnToSurvive
{
    /// <summary>
    /// Joy giver that lets pawns read intelligence books during recreation time.
    /// Provides both joy and intelligence XP.
    /// </summary>
    public class JoyGiver_ReadBook : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return null;
            if (LTSDefOf.LTS_ReadBook == null || LTSDefOf.LTS_Book == null) return null;

            // Find available books
            Thing book = FindBestBook(pawn);
            if (book == null) return null;

            return JobMaker.MakeJob(LTSDefOf.LTS_ReadBook, book);
        }

        public override Job TryGiveJobWhileInBed(Pawn pawn)
        {
            // Can also read while in bed
            return TryGiveJob(pawn);
        }

        private Thing FindBestBook(Pawn pawn)
        {
            var comp = pawn.GetComp<CompIntelligence>();

            Thing bestBook = null;
            float bestScore = -1f;

            List<Thing> books = pawn.Map.listerThings.ThingsOfDef(LTSDefOf.LTS_Book);
            if (books == null) return null;

            foreach (Thing book in books)
            {
                if (book.IsForbidden(pawn)) continue;
                if (!pawn.CanReserve(book)) continue;
                if (!pawn.CanReach(book, PathEndMode.ClosestTouch, Danger.None)) continue;

                CompBook bookComp = book.TryGetComp<CompBook>();
                if (bookComp == null) continue;

                float score = 0f;

                // Prefer books for stats where pawn has lower levels
                if (comp != null)
                {
                    int pawnLevel = comp.GetLevel(bookComp.statType);
                    score += (20 - pawnLevel) * 5f; // More valuable for low-level stats

                    // Prefer books not read many times
                    int readCount = comp.GetBookReadCount(book.thingIDNumber);
                    score -= readCount * 20f;

                    // Prefer enabled stats
                    if (!LTSSettings.IsStatEnabled(bookComp.statType))
                        score -= 1000f;
                }

                // Prefer higher quality books
                score += (int)bookComp.quality * 10f;

                // Prefer closer books
                float dist = pawn.Position.DistanceTo(book.Position);
                score -= dist * 0.5f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestBook = book;
                }
            }

            return bestBook;
        }
    }
}
