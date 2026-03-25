using System;
using RimWorld;
using Verse;

namespace LearnToSurvive
{
    public static class BackstoryMapper
    {
        public static int GetStartingLevel(Pawn pawn, StatType type)
        {
            if (pawn?.story == null) return 0;

            // Base level from technology level of backstory/faction
            TechLevel tech = GetPawnTechLevel(pawn);
            int baseLv = GetBaseFromTech(tech, type);

            // Modify based on specific backstory keywords
            baseLv += GetBackstoryBonus(pawn, type);

            // Modify based on relevant skills
            baseLv += GetSkillBonus(pawn, type);

            // Add some randomness (+/- 1)
            int hash = Gen.HashCombineInt(pawn.thingIDNumber, (int)type);
            int variance = (Math.Abs(hash) % 3) - 1; // -1, 0, or +1
            baseLv += variance;

            return Math.Max(0, Math.Min(baseLv, 8)); // Cap starting level at 8
        }

        private static TechLevel GetPawnTechLevel(Pawn pawn)
        {
            if (pawn.Faction != null)
                return pawn.Faction.def.techLevel;
            if (pawn.story?.Childhood != null)
            {
                // Infer from backstory categories
                // This is approximate
            }
            return TechLevel.Industrial;
        }

        private static int GetBaseFromTech(TechLevel tech, StatType type)
        {
            // Returns base starting level by tech level and stat
            switch (tech)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic:
                    switch (type)
                    {
                        case StatType.HaulingSense: return 0;
                        case StatType.WorkAwareness: return 0;
                        case StatType.PathMemory: return 1;  // Tribals know terrain
                        case StatType.CombatInstinct: return 1; // Survival basics
                        case StatType.SelfPreservation: return 1;
                        default: return 0;
                    }
                case TechLevel.Medieval:
                    switch (type)
                    {
                        case StatType.HaulingSense: return 1;
                        case StatType.WorkAwareness: return 1;
                        case StatType.PathMemory: return 1;
                        case StatType.CombatInstinct: return 2;
                        case StatType.SelfPreservation: return 1;
                        default: return 1;
                    }
                case TechLevel.Industrial:
                    switch (type)
                    {
                        case StatType.HaulingSense: return 2;
                        case StatType.WorkAwareness: return 3;
                        case StatType.PathMemory: return 2;
                        case StatType.CombatInstinct: return 1;
                        case StatType.SelfPreservation: return 3;
                        default: return 2;
                    }
                case TechLevel.Spacer:
                    switch (type)
                    {
                        case StatType.HaulingSense: return 3;
                        case StatType.WorkAwareness: return 4;
                        case StatType.PathMemory: return 3;
                        case StatType.CombatInstinct: return 3;
                        case StatType.SelfPreservation: return 4;
                        default: return 3;
                    }
                case TechLevel.Ultra:
                    switch (type)
                    {
                        case StatType.HaulingSense: return 5;
                        case StatType.WorkAwareness: return 5;
                        case StatType.PathMemory: return 4;
                        case StatType.CombatInstinct: return 2; // Sheltered
                        case StatType.SelfPreservation: return 6;
                        default: return 4;
                    }
                case TechLevel.Archotech:
                    return 6;
                default:
                    return 1;
            }
        }

        private static int GetBackstoryBonus(Pawn pawn, StatType type)
        {
            int bonus = 0;

            // Check childhood and adulthood backstory work tags and descriptions
            if (pawn.story.Childhood != null)
                bonus += CheckBackstoryTags(pawn.story.Childhood, type);
            if (pawn.story.Adulthood != null)
                bonus += CheckBackstoryTags(pawn.story.Adulthood, type);

            return bonus;
        }

        private static int CheckBackstoryTags(BackstoryDef backstory, StatType type)
        {
            if (backstory == null) return 0;

            int bonus = 0;
            var tags = backstory.spawnCategories;

            switch (type)
            {
                case StatType.HaulingSense:
                    if (backstory.workDisables.HasFlag(WorkTags.Hauling))
                        bonus -= 2; // Can't haul = no hauling experience
                    break;

                case StatType.WorkAwareness:
                    if (backstory.workDisables == WorkTags.None)
                        bonus += 1; // No work restrictions = versatile experience
                    break;

                case StatType.CombatInstinct:
                    if (backstory.workDisables.HasFlag(WorkTags.Violent))
                        bonus -= 2; // Pacifist = no combat experience
                    if (tags != null)
                    {
                        foreach (string tag in tags)
                        {
                            if (tag.Contains("Raider") || tag.Contains("Pirate") || tag.Contains("Soldier"))
                                bonus += 2;
                        }
                    }
                    break;

                case StatType.SelfPreservation:
                    // No specific backstory tag affects this much
                    break;

                case StatType.PathMemory:
                    if (tags != null)
                    {
                        foreach (string tag in tags)
                        {
                            if (tag.Contains("Tribal"))
                                bonus += 1; // Tribals are good navigators
                        }
                    }
                    break;
            }

            return bonus;
        }

        private static int GetSkillBonus(Pawn pawn, StatType type)
        {
            if (pawn.skills == null) return 0;

            // High relevant skill gives a small starting bonus
            switch (type)
            {
                case StatType.HaulingSense:
                    return pawn.skills.GetSkill(SkillDefOf.Intellectual).Level >= 10 ? 1 : 0;

                case StatType.WorkAwareness:
                    // Check their best work skill
                    int bestWorkSkill = 0;
                    foreach (var skill in pawn.skills.skills)
                    {
                        if (skill.Level > bestWorkSkill)
                            bestWorkSkill = skill.Level;
                    }
                    return bestWorkSkill >= 12 ? 1 : 0;

                case StatType.CombatInstinct:
                    int shooting = pawn.skills.GetSkill(SkillDefOf.Shooting).Level;
                    int melee = pawn.skills.GetSkill(SkillDefOf.Melee).Level;
                    int best = Math.Max(shooting, melee);
                    if (best >= 15) return 3;
                    if (best >= 10) return 2;
                    if (best >= 5) return 1;
                    return 0;

                case StatType.SelfPreservation:
                    int medical = pawn.skills.GetSkill(SkillDefOf.Medicine).Level;
                    return medical >= 8 ? 1 : 0;

                case StatType.PathMemory:
                    return 0; // No direct skill correlation

                default:
                    return 0;
            }
        }
    }
}
