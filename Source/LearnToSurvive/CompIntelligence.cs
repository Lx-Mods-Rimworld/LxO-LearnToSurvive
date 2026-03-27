using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LearnToSurvive
{
    public class CompProperties_Intelligence : CompProperties
    {
        public CompProperties_Intelligence()
        {
            compClass = typeof(CompIntelligence);
        }
    }

    public class CompIntelligence : ThingComp
    {
        private Dictionary<StatType, IntelligenceData> stats = new Dictionary<StatType, IntelligenceData>();
        private bool initialized;

        // Path memory: track visited regions (regionID -> lastVisitedTick)
        // Region IDs are recycled on rebuild, so entries expire after 60000 ticks (~1 day)
        public Dictionary<int, int> visitedRegions = new Dictionary<int, int>();

        // Hauling inventory tracking: thingIDs of items we're hauling in inventory
        public HashSet<int> haulInventoryItems = new HashSet<int>();

        // Workstation loyalty: defName of preferred workstation -> thingID
        public Dictionary<string, int> preferredStations = new Dictionary<string, int>();

        // Danger memory: cell indices where this pawn was injured, with tick of injury
        public Dictionary<int, int> dangerMemory = new Dictionary<int, int>();

        // Book read tracking: bookThingID -> times read by this pawn
        public Dictionary<int, int> booksReadCount = new Dictionary<int, int>();

        // Path XP accumulator (tiles walked since last XP award)
        public int tilesWalkedAccumulator;

        // Cached trait modifiers
        private Dictionary<StatType, float> traitModifierCache;
        private int traitModifierCacheTick = -1;

        // Tick stagger offset for periodic updates
        private int tickOffset = -1;
        private const int TickInterval = 250;

        public Pawn Pawn
        {
            get
            {
                if (parent is Pawn p) return p;
                if (parent is Corpse c) return c.InnerPawn;
                return null;
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
        }

        private void InitializeStats()
        {
            if (initialized) return;
            initialized = true;

            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                if (!stats.ContainsKey(type))
                {
                    int startLevel = (parent is Pawn) ? BackstoryMapper.GetStartingLevel(Pawn, type) : 0;
                    stats[type] = new IntelligenceData(type, startLevel);
                }
            }
        }

        public int GetLevel(StatType type)
        {
            if (!initialized) InitializeStats();
            if (!LTSSettings.IsStatEnabled(type)) return 0;
            return stats.TryGetValue(type, out var data) ? data.level : 0;
        }

        public IntelligenceData GetStatData(StatType type)
        {
            if (!initialized) InitializeStats();
            return stats.TryGetValue(type, out var data) ? data : null;
        }

        public void AddXP(StatType type, float baseAmount, string source = null)
        {
            if (!initialized) InitializeStats();
            if (!LTSSettings.IsStatEnabled(type)) return;
            if (!stats.TryGetValue(type, out var data)) return;
            if (data.level >= IntelligenceData.MaxLevel) return;
            Pawn pawn = Pawn;
            if (pawn == null) return;

            float modifier = GetTraitModifier(type);
            float intellectBonus = 1f + (pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0) * 0.02f;
            float mentorBonus = GetMentorBonus(type);

            float totalXP = baseAmount * LTSSettings.xpMultiplier * modifier * intellectBonus * mentorBonus;

            int oldLevel = data.level;
            bool leveledUp = data.TryAddXP(totalXP);

            LTSLog.XPGain(pawn, type, baseAmount, totalXP, data.XPProgress, data.level);

            if (leveledUp)
            {
                string tierName = IntelligenceData.GetTierName(type, data.level);
                LTSLog.LevelUp(pawn, type, oldLevel, data.level, tierName);
            }
        }

        private float GetTraitModifier(StatType type)
        {
            int tick = Find.TickManager.TicksGame;
            if (traitModifierCache == null || tick - traitModifierCacheTick > 2500)
            {
                traitModifierCache = TraitModifiers.GetAllModifiers(Pawn);
                traitModifierCacheTick = tick;
            }
            return traitModifierCache.TryGetValue(type, out float mod) ? mod : 1f;
        }

        // Mentor bonus cache (iterates all colonists -- expensive)
        private float cachedMentorBonus = 0f;
        private int mentorBonusCacheTick = -999;
        private StatType cachedMentorBonusType;

        private float GetMentorBonus(StatType type)
        {
            int tick = Find.TickManager.TicksGame;
            if (tick - mentorBonusCacheTick < 500 && cachedMentorBonusType == type)
                return cachedMentorBonus;
            mentorBonusCacheTick = tick;
            cachedMentorBonusType = type;
            cachedMentorBonus = ComputeMentorBonus(type);
            return cachedMentorBonus;
        }

        private float ComputeMentorBonus(StatType type)
        {
            Pawn pawn = Pawn;
            if (pawn?.Map == null) return 1f;

            float bonus = 1f;
            foreach (Pawn other in pawn.Map.mapPawns.FreeColonistsSpawned)
            {
                if (other == pawn) continue;
                if (other.Position.DistanceTo(pawn.Position) > 10f) continue;

                var otherComp = other.GetComp<CompIntelligence>();
                if (otherComp == null) continue;

                int otherLevel = otherComp.GetLevel(type);
                if (otherLevel >= 20)
                {
                    float mentorRadius = 10f;
                    // Kind trait doubles mentor radius
                    if (other.story?.traits?.HasTrait(TraitDefOf.Kind) == true)
                        mentorRadius = 20f;
                    // Abrasive halves it
                    if (other.story?.traits?.allTraits?.Any(t => t.def.defName == "Abrasive") == true)
                        mentorRadius = 5f;

                    if (other.Position.DistanceTo(pawn.Position) <= mentorRadius)
                    {
                        bonus = 1.25f;
                        break;
                    }
                }
            }
            return bonus;
        }

        public void RecordDanger(IntVec3 cell)
        {
            if (Pawn?.Map == null) return;
            int index = Pawn.Map.cellIndices.CellToIndex(cell);
            dangerMemory[index] = Find.TickManager.TicksGame;
        }

        public bool IsDangerCell(IntVec3 cell)
        {
            if (Pawn?.Map == null) return false;
            int index = Pawn.Map.cellIndices.CellToIndex(cell);
            if (!dangerMemory.TryGetValue(index, out int injuryTick)) return false;
            // Remember danger for 5 days (300,000 ticks)
            return Find.TickManager.TicksGame - injuryTick < 300000;
        }

        public int GetBookReadCount(int bookThingID)
        {
            return booksReadCount.TryGetValue(bookThingID, out int count) ? count : 0;
        }

        public void RecordBookRead(int bookThingID)
        {
            if (booksReadCount.ContainsKey(bookThingID))
                booksReadCount[bookThingID]++;
            else
                booksReadCount[bookThingID] = 1;
        }

        /// <summary>
        /// Check if a region was visited recently (within 60000 ticks / ~1 in-game day).
        /// Handles region ID recycling by expiring old entries.
        /// </summary>
        public bool IsRegionFamiliar(int regionId)
        {
            if (!visitedRegions.TryGetValue(regionId, out int visitTick)) return false;
            return Find.TickManager.TicksGame - visitTick < 60000;
        }

        public override void CompTick()
        {
            base.CompTick();

            if (tickOffset < 0)
                tickOffset = parent.thingIDNumber % TickInterval;

            if ((Find.TickManager.TicksGame + tickOffset) % TickInterval != 0) return;

            if (!(parent is Pawn)) return; // Skip corpses
            if (!initialized) InitializeStats();
            if (Pawn.Dead || Pawn.Downed) return;

            // Skip non-player pawns (guests, prisoners), allow slaves
            Faction playerFaction = Find.FactionManager?.OfPlayer;
            if (playerFaction == null || (Pawn.Faction != playerFaction && !Pawn.IsSlave)) return;
            if (Pawn.IsPrisoner) return;

            // Award passive path XP
            if (LTSSettings.enablePathMemory && tilesWalkedAccumulator >= 10)
            {
                int chunks = tilesWalkedAccumulator / 10;
                AddXP(StatType.PathMemory, 0.5f * chunks, "walking");
                tilesWalkedAccumulator = tilesWalkedAccumulator % 10;
            }

            // Track current region for path familiarity
            if (LTSSettings.enablePathMemory && Pawn.Map != null && Pawn.Position.InBounds(Pawn.Map))
            {
                var region = Pawn.Position.GetRegion(Pawn.Map);
                if (region != null)
                    visitedRegions[region.id] = Find.TickManager.TicksGame;
            }

            // Clean up old danger memory (every 2500 ticks = ~42 seconds)
            if ((Find.TickManager.TicksGame + tickOffset) % 2500 == 0)
            {
                CleanDangerMemory();
            }

            // Self-preservation: check if needs are being satisfied efficiently
            if (LTSSettings.enableSelfPreservation)
            {
                CheckNeedSatisfaction();
            }
        }

        private static readonly List<int> keysToRemove = new List<int>();

        private void CleanDangerMemory()
        {
            keysToRemove.Clear();
            int tick = Find.TickManager.TicksGame;
            foreach (var kvp in dangerMemory)
                if (tick - kvp.Value > 300000) keysToRemove.Add(kvp.Key);
            for (int i = 0; i < keysToRemove.Count; i++)
                dangerMemory.Remove(keysToRemove[i]);

            // Clean stale visited regions (>1 day / 60000 ticks old)
            keysToRemove.Clear();
            foreach (var kvp in visitedRegions)
                if (tick - kvp.Value > 60000) keysToRemove.Add(kvp.Key);
            for (int i = 0; i < keysToRemove.Count; i++)
                visitedRegions.Remove(keysToRemove[i]);
        }

        private void CheckNeedSatisfaction()
        {
            if (Pawn.needs == null) return;

            // Award XP when needs are satisfied above threshold (efficient self-care)
            var food = Pawn.needs.food;
            if (food != null && food.CurLevelPercentage > 0.8f && food.CurLevelPercentage < 0.85f)
            {
                // Pawn ate before getting too hungry - reward
                AddXP(StatType.SelfPreservation, 3f, "efficient_eating");
            }

            var rest = Pawn.needs.rest;
            if (rest != null && rest.CurLevelPercentage > 0.7f && rest.CurLevelPercentage < 0.75f)
            {
                AddXP(StatType.SelfPreservation, 3f, "efficient_rest");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (!(parent is Pawn) && !(parent is Corpse)) return;

            Scribe_Values.Look(ref initialized, "lts_initialized", false);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (!initialized) InitializeStats();
                var statList = stats.Values.ToList();
                Scribe_Collections.Look(ref statList, "lts_stats", LookMode.Deep);
            }
            else
            {
                List<IntelligenceData> statList = null;
                Scribe_Collections.Look(ref statList, "lts_stats", LookMode.Deep);
                if (statList != null)
                {
                    stats = new Dictionary<StatType, IntelligenceData>();
                    foreach (var data in statList)
                        stats[data.statType] = data;
                }
            }

            // Cap visitedRegions before saving to limit save bloat
            if (Scribe.mode == LoadSaveMode.Saving && visitedRegions.Count > 200)
            {
                var sorted = visitedRegions.OrderByDescending(kvp => kvp.Value).Take(200);
                var trimmed = new Dictionary<int, int>();
                foreach (var kvp in sorted)
                    trimmed[kvp.Key] = kvp.Value;
                visitedRegions = trimmed;
            }

            Scribe_Collections.Look(ref visitedRegions, "lts_visitedRegions", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref dangerMemory, "lts_dangerMemory", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref booksReadCount, "lts_booksReadCount", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref preferredStations, "lts_preferredStations", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref tilesWalkedAccumulator, "lts_tilesWalked", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (visitedRegions == null) visitedRegions = new Dictionary<int, int>();
                if (dangerMemory == null) dangerMemory = new Dictionary<int, int>();
                if (booksReadCount == null) booksReadCount = new Dictionary<int, int>();
                if (preferredStations == null) preferredStations = new Dictionary<string, int>();
                if (stats == null) stats = new Dictionary<StatType, IntelligenceData>();
                haulInventoryItems = new HashSet<int>();
                initialized = true;
                // Ensure all stat types exist
                foreach (StatType type in Enum.GetValues(typeof(StatType)))
                {
                    if (!stats.ContainsKey(type))
                    {
                        int lvl = (parent is Pawn) ? BackstoryMapper.GetStartingLevel(Pawn, type) : 0;
                        stats[type] = new IntelligenceData(type, lvl);
                    }
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!(parent is Pawn)) return null;
            if (!initialized || Pawn.Dead) return null;

            int avg = 0;
            int count = 0;
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                if (LTSSettings.IsStatEnabled(type))
                {
                    avg += GetLevel(type);
                    count++;
                }
            }
            if (count == 0) return null;
            avg = avg / count;

            return "LTS_InspectString".Translate(avg);
        }
    }
}
