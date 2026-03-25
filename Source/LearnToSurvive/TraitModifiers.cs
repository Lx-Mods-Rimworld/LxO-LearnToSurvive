using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LearnToSurvive
{
    public static class TraitModifiers
    {
        // (traitDefName, degree) -> list of (statType, multiplier addition)
        // e.g. +0.20 means 20% faster learning, -0.15 means 15% slower
        private static readonly List<TraitModifierEntry> entries = new List<TraitModifierEntry>
        {
            // Industriousness: Industrious (+2), Hard Worker (+1), Lazy (-1), Slothful (-2)
            new TraitModifierEntry("Industriousness", 2, StatType.WorkAwareness, 0.20f),
            new TraitModifierEntry("Industriousness", 1, StatType.WorkAwareness, 0.10f),
            new TraitModifierEntry("Industriousness", -1, StatType.WorkAwareness, -0.10f),
            new TraitModifierEntry("Industriousness", -2, StatType.WorkAwareness, -0.20f),

            // SpeedOffset: Jogger (+2), Fast Walker (+1), Slowpoke (-1)
            new TraitModifierEntry("SpeedOffset", 2, StatType.PathMemory, 0.20f),
            new TraitModifierEntry("SpeedOffset", 1, StatType.PathMemory, 0.10f),
            new TraitModifierEntry("SpeedOffset", -1, StatType.PathMemory, -0.10f),

            // Combat traits
            new TraitModifierEntry("Brawler", 0, StatType.CombatInstinct, 0.20f),
            new TraitModifierEntry("Tough", 0, StatType.CombatInstinct, 0.15f),
            new TraitModifierEntry("Wimp", 0, StatType.CombatInstinct, -0.25f),
            new TraitModifierEntry("Wimp", 0, StatType.SelfPreservation, 0.15f),
            new TraitModifierEntry("Psychopath", 0, StatType.CombatInstinct, 0.15f),
            new TraitModifierEntry("Bloodlust", 0, StatType.CombatInstinct, 0.20f),

            // ShootingAccuracy: Careful Shooter (+2), Trigger-Happy (-1)
            new TraitModifierEntry("ShootingAccuracy", 2, StatType.CombatInstinct, 0.15f),
            new TraitModifierEntry("ShootingAccuracy", -1, StatType.CombatInstinct, -0.10f),

            // Self-preservation related
            new TraitModifierEntry("Ascetic", 0, StatType.SelfPreservation, -0.10f),
            new TraitModifierEntry("Gourmand", 0, StatType.SelfPreservation, 0.15f),

            // Nerves: Iron-Willed (+2), Steadfast (+1), Nervous (-1), Volatile (-2)
            new TraitModifierEntry("Nerves", 2, StatType.SelfPreservation, 0.15f),
            new TraitModifierEntry("Nerves", 1, StatType.SelfPreservation, 0.10f),
            new TraitModifierEntry("Nerves", -1, StatType.SelfPreservation, -0.05f),
            new TraitModifierEntry("Nerves", -2, StatType.SelfPreservation, -0.15f),
            new TraitModifierEntry("Nerves", -2, StatType.CombatInstinct, 0.10f), // volatile but aggressive

            // NaturalMood: Sanguine (+2), Optimist (+1), Pessimist (-1), Depressive (-2)
            new TraitModifierEntry("NaturalMood", 2, StatType.SelfPreservation, -0.10f), // already happy
            new TraitModifierEntry("NaturalMood", -2, StatType.SelfPreservation, 0.15f), // needs self-care
            new TraitModifierEntry("NaturalMood", -1, StatType.SelfPreservation, 0.05f),

            // NightOwl
            new TraitModifierEntry("NightOwl", 0, StatType.WorkAwareness, 0.05f),

            // Nimble
            new TraitModifierEntry("Nimble", 0, StatType.PathMemory, 0.10f),
            new TraitModifierEntry("Nimble", 0, StatType.CombatInstinct, 0.10f),

            // TooSmart (all stats)
            new TraitModifierEntry("TooSmart", 0, StatType.HaulingSense, 0.15f),
            new TraitModifierEntry("TooSmart", 0, StatType.WorkAwareness, 0.15f),
            new TraitModifierEntry("TooSmart", 0, StatType.PathMemory, 0.15f),
            new TraitModifierEntry("TooSmart", 0, StatType.CombatInstinct, 0.15f),
            new TraitModifierEntry("TooSmart", 0, StatType.SelfPreservation, 0.15f),

            // GreenThumb
            new TraitModifierEntry("GreenThumb", 0, StatType.WorkAwareness, 0.10f),

            // Pyromaniac: doesn't learn fire avoidance as well
            new TraitModifierEntry("Pyromaniac", 0, StatType.CombatInstinct, -0.10f),
        };

        // Check for global learning traits
        private static readonly List<GlobalTraitModifier> globalEntries = new List<GlobalTraitModifier>
        {
            // Fast Learner / Slow Learner if they exist as traits
            new GlobalTraitModifier("FastLearner", 0, 0.30f),
            new GlobalTraitModifier("SlowLearner", 0, -0.30f),
        };

        public static Dictionary<StatType, float> GetAllModifiers(Pawn pawn)
        {
            var result = new Dictionary<StatType, float>();
            foreach (StatType type in System.Enum.GetValues(typeof(StatType)))
                result[type] = 1f;

            if (pawn?.story?.traits == null) return result;

            float globalMod = 0f;

            // Check global learning traits
            foreach (var global in globalEntries)
            {
                var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(global.traitDefName);
                if (traitDef != null && pawn.story.traits.HasTrait(traitDef, global.degree))
                    globalMod += global.allStatsModifier;
            }

            // Also use vanilla's learning rate stat if available
            float vanillaLearning = pawn.GetStatValue(StatDefOf.GlobalLearningFactor, true, -1) - 1f;
            globalMod += vanillaLearning * 0.5f; // Use half of vanilla's learning factor

            // Check per-stat trait modifiers
            foreach (var entry in entries)
            {
                var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(entry.traitDefName);
                if (traitDef == null) continue;
                if (!pawn.story.traits.HasTrait(traitDef, entry.degree)) continue;

                result[entry.statType] += entry.modifier;
            }

            // Apply global modifier to all stats
            if (globalMod != 0f)
            {
                foreach (StatType type in System.Enum.GetValues(typeof(StatType)))
                    result[type] += globalMod;
            }

            // Clamp all modifiers to [0.1, 3.0] range
            foreach (StatType type in System.Enum.GetValues(typeof(StatType)))
                result[type] = UnityEngine.Mathf.Clamp(result[type], 0.1f, 3.0f);

            return result;
        }

        private struct TraitModifierEntry
        {
            public string traitDefName;
            public int degree;
            public StatType statType;
            public float modifier;

            public TraitModifierEntry(string def, int deg, StatType stat, float mod)
            {
                traitDefName = def;
                degree = deg;
                statType = stat;
                modifier = mod;
            }
        }

        private struct GlobalTraitModifier
        {
            public string traitDefName;
            public int degree;
            public float allStatsModifier;

            public GlobalTraitModifier(string def, int deg, float mod)
            {
                traitDefName = def;
                degree = deg;
                allStatsModifier = mod;
            }
        }
    }
}
