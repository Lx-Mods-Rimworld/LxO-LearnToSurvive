using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LearnToSurvive
{
    /// <summary>
    /// Tracks items hauled into a pawn's inventory by our SmartHaul system.
    /// Persists across jobs, save/load, and interruptions.
    /// Items stay in inventory until explicitly unloaded by the unload job.
    /// Pattern from PUAH: track Thing references, auto-clean nulls.
    /// </summary>
    public class CompHauledItems : ThingComp
    {
        private HashSet<Thing> hauledThings = new HashSet<Thing>();

        /// <summary>
        /// Get the set of tracked hauled items, cleaning up nulls/destroyed items.
        /// </summary>
        public HashSet<Thing> GetHauledThings()
        {
            hauledThings.RemoveWhere(t => t == null || t.Destroyed);
            return hauledThings;
        }

        public void RegisterHauledItem(Thing thing)
        {
            if (thing != null)
            {
                hauledThings.Add(thing);
                Log.Message($"[LTS-Comp] {parent.LabelShort}: registered {thing.LabelShort} x{thing.stackCount} (total tracked={hauledThings.Count})");
            }
        }

        public void UnregisterHauledItem(Thing thing)
        {
            bool removed = hauledThings.Remove(thing);
            if (removed)
                Log.Message($"[LTS-Comp] {parent.LabelShort}: unregistered {thing?.LabelShort ?? "NULL"} (remaining={hauledThings.Count})");
        }

        public bool IsTracked(Thing thing)
        {
            return thing != null && hauledThings.Contains(thing);
        }

        public int Count => hauledThings.Count;

        public bool HasItems
        {
            get
            {
                if (hauledThings.Count == 0) return false;
                // Clean and re-check
                hauledThings.RemoveWhere(t => t == null || t.Destroyed);
                return hauledThings.Count > 0;
            }
        }

        /// <summary>
        /// Find the first hauled item still in the pawn's inventory.
        /// Handles merging: if a tracked Thing isn't in inventory anymore,
        /// look for an item with the same def (it got merged).
        /// </summary>
        public Thing FirstUnloadableThing(Pawn pawn)
        {
            var inv = pawn.inventory.innerContainer;
            var tracked = GetHauledThings();

            // Direct match first
            foreach (Thing t in tracked)
            {
                if (inv.Contains(t))
                {
                    Log.Message($"[LTS-Comp] {pawn.LabelShort}: FirstUnloadableThing direct match={t.LabelShort} x{t.stackCount}");
                    return t;
                }
            }

            // Merged item: tracked Thing not in inventory, but same def exists
            foreach (Thing t in tracked)
            {
                for (int i = 0; i < inv.Count; i++)
                {
                    if (inv[i].def == t.def)
                    {
                        // Replace the stale reference with the merged one
                        Log.Message($"[LTS-Comp] {pawn.LabelShort}: FirstUnloadableThing merge detected, stale={t.LabelShort} -> merged={inv[i].LabelShort} x{inv[i].stackCount}");
                        hauledThings.Remove(t);
                        hauledThings.Add(inv[i]);
                        return inv[i];
                    }
                }
            }

            // Nothing found -- clear stale references
            Log.Message($"[LTS-Comp] {pawn.LabelShort}: FirstUnloadableThing found nothing, clearing {hauledThings.Count} stale refs");
            hauledThings.Clear();
            return null;
        }

        /// <summary>
        /// Check if any tracked items are about to rot (within 8 hours game time).
        /// </summary>
        public bool HasPerishableSoon(Pawn pawn)
        {
            var inv = pawn.inventory.innerContainer;
            foreach (Thing t in GetHauledThings())
            {
                if (!inv.Contains(t)) continue;
                CompRottable rot = t.TryGetComp<CompRottable>();
                if (rot != null && rot.TicksUntilRotAtCurrentTemp < 30000) // ~8 hours
                    return true;
            }
            return false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Save/load tracked items as references
            var list = hauledThings.ToList();
            Scribe_Collections.Look(ref list, "hauledThings", LookMode.Reference);
            hauledThings = list != null ? new HashSet<Thing>(list.Where(t => t != null)) : new HashSet<Thing>();
        }
    }

    public class CompProperties_HauledItems : CompProperties
    {
        public CompProperties_HauledItems()
        {
            compClass = typeof(CompHauledItems);
        }
    }
}
