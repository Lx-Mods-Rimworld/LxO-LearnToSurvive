using RimWorld;
using Verse;

namespace LearnToSurvive
{
    public class StatPart_WorkstationLoyalty : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!LTSSettings.enableWorkAwareness) return;
            if (!(req.Thing is Pawn pawn)) return;
            if (pawn.Map == null) return;
            var comp = pawn.GetComp<CompIntelligence>();
            if (comp == null) return;
            int level = comp.GetLevel(StatType.WorkAwareness);
            float bonus = WorkAwareness.WorkstationSpeedBonus(level);
            if (bonus <= 0f) return;
            if (!WorkAwareness.IsPreferredStation(pawn, comp)) return;
            val *= 1f + bonus;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!LTSSettings.enableWorkAwareness) return null;
            if (!(req.Thing is Pawn pawn)) return null;
            if (pawn.Map == null) return null;
            var comp = pawn.GetComp<CompIntelligence>();
            if (comp == null) return null;
            int level = comp.GetLevel(StatType.WorkAwareness);
            float bonus = WorkAwareness.WorkstationSpeedBonus(level);
            if (bonus <= 0f) return null;
            if (!WorkAwareness.IsPreferredStation(pawn, comp)) return null;
            return "LTS_WorkstationLoyalty".Translate() + ": +" + (bonus * 100f).ToString("F0") + "%";
        }
    }
}
