using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LearnToSurvive
{
    [StaticConstructorOnStartup]
    public class ITab_Intelligence : ITab
    {
        private Vector2 scrollPosition;
        private const float TabWidth = 440f;
        private const float TabHeight = 520f;

        public ITab_Intelligence()
        {
            size = new Vector2(TabWidth, TabHeight);
            labelKey = "LTS_Tab_Intelligence";
        }

        public override bool IsVisible
        {
            get
            {
                Pawn pawn = SelPawn;
                if (pawn == null || pawn.Dead) return false;
                return pawn.GetComp<CompIntelligence>() != null;
            }
        }

        private new Pawn SelPawn
        {
            get
            {
                if (SelThing is Pawn p) return p;
                return null;
            }
        }

        protected override void FillTab()
        {
            Pawn pawn = SelPawn;
            if (pawn == null) return;

            var comp = pawn.GetComp<CompIntelligence>();
            if (comp == null) return;

            Rect outRect = new Rect(0f, 0f, TabWidth, TabHeight).ContractedBy(10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, CalculateHeight(comp));

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float curY = 0f;
            float width = viewRect.width;

            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, curY, width, 30f), "LTS_Tab_Title".Translate());
            Text.Font = GameFont.Small;
            curY += 35f;

            // Draw each stat
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                if (!LTSSettings.IsStatEnabled(type)) continue;

                var data = comp.GetStatData(type);
                if (data == null) continue;

                curY = DrawStat(data, type, width, curY);
                curY += 10f;
            }

            // Trait modifiers section
            curY += 5f;
            Widgets.DrawLineHorizontal(0f, curY, width);
            curY += 5f;

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.8f, 0.8f, 0.6f);
            Widgets.Label(new Rect(0f, curY, width, 20f), "LTS_TraitModifiers".Translate());
            GUI.color = Color.white;
            curY += 22f;

            var modifiers = TraitModifiers.GetAllModifiers(pawn);
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                if (!modifiers.TryGetValue(type, out float mod)) continue;
                if (Math.Abs(mod - 1f) < 0.01f) continue; // No modifier

                string modStr = mod > 1f ? "+" + ((mod - 1f) * 100f).ToString("F0") + "%" :
                    ((mod - 1f) * 100f).ToString("F0") + "%";
                Color color = mod > 1f ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.8f, 0.4f, 0.4f);

                GUI.color = color;
                Widgets.Label(new Rect(10f, curY, width - 10f, 18f),
                    IntelligenceData.GetStatLabel(type) + ": " + modStr);
                GUI.color = Color.white;
                curY += 18f;
            }

            Widgets.EndScrollView();
        }

        private float DrawStat(IntelligenceData data, StatType type, float width, float startY)
        {
            float curY = startY;

            // Stat name and level
            string label = IntelligenceData.GetStatLabel(type);
            string levelStr = data.level + "/" + IntelligenceData.MaxLevel;

            Text.Font = GameFont.Small;
            GUI.color = GetStatColor(type);
            Widgets.Label(new Rect(0f, curY, width * 0.7f, 22f), label);
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(width * 0.7f, curY, width * 0.3f, 22f), levelStr);
            Text.Anchor = TextAnchor.UpperLeft;
            curY += 22f;

            // XP progress bar
            Rect barRect = new Rect(0f, curY, width, 16f);
            if (data.level < IntelligenceData.MaxLevel)
            {
                Widgets.FillableBar(barRect, data.XPProgress, GetStatBarTex(type), BaseContent.BlackTex, false);

                // XP text on bar
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.white;
                string xpText = data.xp.ToString("F0") + " / " + data.XPForNextLevel.ToString("F0") + " XP";
                Widgets.Label(barRect, xpText);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
            else
            {
                Widgets.FillableBar(barRect, 1f, GetStatBarTex(type), BaseContent.BlackTex, false);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(barRect, "LTS_Mastered".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
            curY += 18f;

            // Tier name
            string tierName = IntelligenceData.GetTierName(type, data.level);
            GUI.color = new Color(0.7f, 0.85f, 1f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, curY, width, 20f), tierName);
            curY += 18f;

            // Current ability description
            string abilityDesc = data.GetCurrentAbilityDescription();
            if (!string.IsNullOrEmpty(abilityDesc))
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                float textHeight = Text.CalcHeight("> " + abilityDesc, width - 10f);
                Widgets.Label(new Rect(5f, curY, width - 10f, textHeight), "> " + abilityDesc);
                curY += textHeight + 2f;
            }

            // Next unlock
            if (data.level < IntelligenceData.MaxLevel)
            {
                string nextDesc = data.GetNextAbilityDescription();
                if (!string.IsNullOrEmpty(nextDesc))
                {
                    string nextText = "LTS_NextUnlock".Translate(data.level + 1) + ": " + nextDesc;
                    GUI.color = new Color(0.4f, 0.4f, 0.4f);
                    float textHeight = Text.CalcHeight(nextText, width - 10f);
                    Widgets.Label(new Rect(5f, curY, width - 10f, textHeight), nextText);
                    curY += textHeight + 2f;
                }
            }

            GUI.color = Color.white;
            return curY;
        }

        private float CalculateHeight(CompIntelligence comp)
        {
            float height = 45f; // Title
            int enabledStats = 0;
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                if (LTSSettings.IsStatEnabled(type))
                    enabledStats++;
            }
            height += enabledStats * 100f; // ~100 per stat
            height += 100f; // Trait modifiers section
            return height;
        }

        private Color GetStatColor(StatType type)
        {
            switch (type)
            {
                case StatType.HaulingSense: return new Color(0.9f, 0.7f, 0.3f); // Orange
                case StatType.WorkAwareness: return new Color(0.3f, 0.8f, 0.3f); // Green
                case StatType.PathMemory: return new Color(0.4f, 0.6f, 0.9f); // Blue
                case StatType.CombatInstinct: return new Color(0.9f, 0.3f, 0.3f); // Red
                case StatType.SelfPreservation: return new Color(0.8f, 0.5f, 0.8f); // Purple
                default: return Color.white;
            }
        }

        private static Texture2D haulingBar, workBar, pathBar, combatBar, selfPresBar;

        private Texture2D GetStatBarTex(StatType type)
        {
            switch (type)
            {
                case StatType.HaulingSense:
                    if (haulingBar == null) haulingBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.9f, 0.7f, 0.3f, 0.6f));
                    return haulingBar;
                case StatType.WorkAwareness:
                    if (workBar == null) workBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.3f, 0.8f, 0.3f, 0.6f));
                    return workBar;
                case StatType.PathMemory:
                    if (pathBar == null) pathBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.4f, 0.6f, 0.9f, 0.6f));
                    return pathBar;
                case StatType.CombatInstinct:
                    if (combatBar == null) combatBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.9f, 0.3f, 0.3f, 0.6f));
                    return combatBar;
                case StatType.SelfPreservation:
                    if (selfPresBar == null) selfPresBar = SolidColorMaterials.NewSolidColorTexture(new Color(0.8f, 0.5f, 0.8f, 0.6f));
                    return selfPresBar;
                default:
                    return BaseContent.WhiteTex;
            }
        }
    }
}
