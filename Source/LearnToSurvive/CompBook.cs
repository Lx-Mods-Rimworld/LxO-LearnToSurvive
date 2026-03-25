using RimWorld;
using Verse;

namespace LearnToSurvive
{
    public class CompProperties_Book : CompProperties
    {
        public CompProperties_Book()
        {
            compClass = typeof(CompBook);
        }
    }

    public class CompBook : ThingComp
    {
        public StatType statType;
        public string authorName = "Unknown";
        public string bookTitle = "Untitled";
        public QualityCategory quality = QualityCategory.Normal;
        public int authorThingID; // For tracking

        public float BaseXPPerRead
        {
            get
            {
                switch (quality)
                {
                    case QualityCategory.Awful: return 10f;
                    case QualityCategory.Poor: return 15f;
                    case QualityCategory.Normal: return 25f;
                    case QualityCategory.Good: return 35f;
                    case QualityCategory.Excellent: return 50f;
                    case QualityCategory.Masterwork: return 70f;
                    case QualityCategory.Legendary: return 100f;
                    default: return 25f;
                }
            }
        }

        public float GetXPForReader(Pawn reader)
        {
            float baseXP = BaseXPPerRead;
            var comp = reader.GetComp<CompIntelligence>();
            if (comp == null) return baseXP;

            // Diminishing returns for re-reading same book
            int readCount = comp.GetBookReadCount(parent.thingIDNumber);
            float diminishing = 1f / (1f + readCount * 0.5f);

            // Less XP if reader's level is already high in this stat
            int readerLevel = comp.GetLevel(statType);
            float levelPenalty = 1f - (readerLevel * 0.03f); // -3% per level
            if (levelPenalty < 0.1f) levelPenalty = 0.1f;

            return baseXP * diminishing * levelPenalty;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref statType, "lts_bookStatType");
            Scribe_Values.Look(ref authorName, "lts_bookAuthor", "Unknown");
            Scribe_Values.Look(ref bookTitle, "lts_bookTitle", "Untitled");
            Scribe_Values.Look(ref quality, "lts_bookQuality", QualityCategory.Normal);
            Scribe_Values.Look(ref authorThingID, "lts_bookAuthorID", 0);
        }

        public override string CompInspectStringExtra()
        {
            return "LTS_BookInspect".Translate(
                IntelligenceData.GetStatLabel(statType),
                authorName,
                quality.GetLabel(),
                BaseXPPerRead.ToString("F0"));
        }

        public override string TransformLabel(string label)
        {
            if (!string.IsNullOrEmpty(bookTitle))
                return bookTitle;
            return label;
        }
    }
}
