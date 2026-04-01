using RimWorld;
using Verse;

namespace LearnToSurvive
{
    [DefOf]
    public static class LTSDefOf
    {
        public static JobDef LTS_SmartHaul;
        public static JobDef LTS_UnloadHauledItems;
        public static JobDef LTS_WriteBook;
        public static JobDef LTS_ReadBook;
        public static ThingDef LTS_Book;
        public static ThingDef LTS_WritingSpot;
        public static JoyKindDef LTS_Reading;

        static LTSDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(LTSDefOf));
        }
    }
}
