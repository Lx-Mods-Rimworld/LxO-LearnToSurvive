using UnityEngine;
using Verse;

namespace LearnToSurvive
{
    public class LearnToSurviveMod : Mod
    {
        public static LTSSettings settings;

        public LearnToSurviveMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<LTSSettings>();
            Log.Message("[LearnToSurvive] v1.0 loaded. 5 intelligence stats, 100 behavioral improvements.");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            LTSSettings.DrawSettings(inRect);
        }

        public override string SettingsCategory()
        {
            return "LxO - Learn to Survive";
        }
    }
}
