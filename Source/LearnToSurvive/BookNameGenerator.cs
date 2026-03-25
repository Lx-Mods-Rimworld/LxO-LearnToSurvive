using System.Collections.Generic;
using Verse;

namespace LearnToSurvive
{
    public static class BookNameGenerator
    {
        private static readonly Dictionary<StatType, List<string>> templates =
            new Dictionary<StatType, List<string>>
        {
            {
                StatType.HaulingSense, new List<string>
                {
                    "{0}'s Guide to Efficient Hauling",
                    "The Art of Carrying: by {0}",
                    "Haul Smarter, Not Harder",
                    "{0}'s Logistics Handbook",
                    "Paths and Loads: A Hauler's Memoir",
                    "From One to Many: {0}'s Hauling Treatise",
                    "The Efficient Porter",
                    "{0}'s Supply Chain Manual",
                    "Carry More, Walk Less",
                    "The Master Hauler's Compendium"
                }
            },
            {
                StatType.WorkAwareness, new List<string>
                {
                    "{0}'s Workshop Manual",
                    "Working Wisdom: by {0}",
                    "The Efficient Craftsperson",
                    "{0}'s Guide to Productive Labor",
                    "Task and Purpose: Lessons Learned",
                    "The Clean Kitchen Principle",
                    "{0}'s Workflow Treatise",
                    "Before You Begin: A Worker's Guide",
                    "Chaining Tasks: {0}'s Method",
                    "The Prepared Artisan"
                }
            },
            {
                StatType.PathMemory, new List<string>
                {
                    "{0}'s Map of the Colony",
                    "Pathways and Shortcuts: by {0}",
                    "The Navigator's Guide",
                    "{0}'s Route Compendium",
                    "Walking Wisdom: A Traveler's Notes",
                    "Know Your Colony: Paths and Places",
                    "{0}'s Terrain Manual",
                    "The Shortcut Collector",
                    "Doors, Halls, and Corridors",
                    "Weather-Wise Walking: by {0}"
                }
            },
            {
                StatType.CombatInstinct, new List<string>
                {
                    "{0}'s Combat Field Manual",
                    "Surviving the Rim: by {0}",
                    "The Art of War on the Rim",
                    "{0}'s Tactical Handbook",
                    "Blood and Cover: Combat Lessons",
                    "Fire Discipline: {0}'s Guide",
                    "When to Retreat: A Soldier's Wisdom",
                    "{0}'s Battle Memoir",
                    "The Survivor's Combat Manual",
                    "Shields, Shots, and Sandbags"
                }
            },
            {
                StatType.SelfPreservation, new List<string>
                {
                    "{0}'s Survival Handbook",
                    "Eating Well, Living Well: by {0}",
                    "The Self-Care Manual",
                    "{0}'s Guide to Staying Alive",
                    "Mind and Body: A Survivor's Notes",
                    "Tables, Meals, and Mood: by {0}",
                    "{0}'s Health Primer",
                    "Listen to Your Body",
                    "The Art of Not Dying",
                    "Comfort and Survival: {0}'s Treatise"
                }
            }
        };

        public static string GenerateTitle(StatType stat, string authorName)
        {
            if (!templates.TryGetValue(stat, out var list) || list.Count == 0)
                return authorName + "'s Manual";

            int index = System.Math.Abs(Gen.HashCombineInt(authorName.GetHashCode(), (int)stat)) % list.Count;
            return string.Format(list[index], authorName);
        }
    }
}
