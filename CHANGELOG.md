# Changelog

All notable changes to this mod will be documented in this file.

## [2.0.1] - 2026-03-29

### Fixes
- Fixed hauling items to wrong stockpile: pawns now track their target stockpile and only pick up items matching that storage filter
- Fixed books getting stuck in pawn inventory when reading is interrupted. Books are now properly dropped.
- Fixed task chaining starvation: raised food/rest/joy thresholds so pawns eat and sleep before chaining another task
- Fixed infinite waiting near miners with no haulable output (now retries 3 times then moves on)
- Fixed valid stacking cells being incorrectly rejected, preventing items from being stored

## [2.0.0] - 2026-03-27 -- Engine Overhaul

**Major internal rework. Every system audited against RimWorld 1.6 engine docs and rebuilt correctly.**

### Behavioral Redesign
- Removed proximity sorting of work items -- was actively slowing down work assignment. Vanilla's region search is already optimal.
- Crafting task chaining no longer modifies bill settings. Products haul normally between iterations. Safe for multiplayer and shared bills.
- Pre-clean now uses a proper queued Clean job instead of injecting toils (which broke vanilla crafting). Pawns get cleaning skill XP and animation.
- Workstation speed bonus now uses RimWorld's StatPart system instead of patching every stat calculation. Shows bonus in stat tooltip.
- Path cost modification removed (was silently doing nothing in 1.6 due to Burst async pathfinding). Tile walking XP and danger memory still work.
- Friendly fire check rewritten to match vanilla's actual projectile intercept model (body size, distance, prone factor) instead of cone approximation.
- Cover seeking now uses directional cover from the enemy position instead of general surrounding cover score.

### Fixes
- Fixed crash on pawn death: intelligence comp now safely handles corpses (reported by multiple users)
- Fixed task chaining causing starvation: colonists now stop chaining when food/rest/joy is critical (reported by @Yaximbahps)
- Fixed books getting stuck in pawn inventory from SmartHaul. Books now use vanilla carry. (reported by @Yaximbahps)
- Fixed stone chunks causing pawns to stand still. Heavy items (>8kg) now use vanilla carry. (reported by @Yaximbahps)
- Fixed cover repositioning overriding player's drafted commands
- Fixed guest/visitor/prisoner pawns incorrectly receiving intelligence behaviors
- Fixed save file bloat from unbounded region memory. Now expires after 1 day and caps at 200 entries.
- Fixed region ID recycling causing stale path familiarity data
- Fixed save compatibility with old saves (visitedRegions format migration)

### Performance
- Replaced all Traverse reflection calls with cached FieldRef delegates (3 hot paths)
- Fixed O(n^2) mass calculation in item batching (running total instead of re-sum)
- Removed LINQ from all hot paths (combat tick, danger cleanup)
- Cached mentor bonus calculation (500-tick expiry instead of per-XP-event)
- Staggered combat XP ticks across pawns to prevent frame spikes

## [1.1.0] - 2026-03-27

### Features
- Crafting task chaining (WorkAwareness Level 8+): pawns now cook/craft multiple bill iterations before hauling results. Products drop near the workstation and get batch-hauled after the last iteration. Example: need 6 meals? Cook all 6, then haul all at once.

### Fixes
- Fixed items permanently stuck in pawn inventory after SmartHaul is interrupted. Hauled items are now dropped back on the ground if the hauling job is interrupted for any reason (combat, recreation, priority change).

## [1.0.2] - 2026-03-26

### Fixes
- Fixed a crash (10 jobs in one tick) caused by SmartHaul trying to haul corpses. Corpses are now skipped during inventory hauling.
- Fixed errors when pawns die: intelligence stats no longer run on corpses, preventing crashes during pawn death and caravan loading.

## [1.0.1] - 2026-03-26

### Fixes
- Fixed Path Memory XP being awarded constantly even when pawns were standing still. XP now only increases when actually walking to a new tile.

## [1.0.0] - 2026-03-26

### Features
- 5 learnable intelligence stats: Hauling Sense, Work Awareness, Path Memory, Combat Instinct, Self-Preservation
- 20 levels per stat, each adding a specific behavioral improvement
- Level 0 = vanilla behavior, no downgrades
- Skills never decay -- veteran colonists are irreplaceable
- Backstory and traits affect starting levels and learning speed
- Level 20 masters can write educational books for others to read
- Books provide both joy and intelligence XP during recreation
- Full settings panel: per-stat toggles, XP multiplier, log level
- Intelligence tab on pawn info panel with colored progress bars
- 7 languages: English, German, Chinese Simplified, Japanese, Korean, Russian, Spanish

### Hauling
- Inventory-based multi-type hauling: pawns pick up different item types in one trip
- Rescans from current position after each pickup for maximum efficiency
- Respects pawn mass capacity, not just stack limits
- Waits for nearby workers (deconstructing, mining) before running to storage with a partial load
- Weapons and apparel excluded from inventory hauling to prevent equip conflicts

### Work
- Pre-task cleaning before cooking and crafting
- Task chaining: deconstruct/harvest/mine multiple targets before hauling results
- Batch construction material delivery: carries enough for multiple frames
- Proximity-aware work selection
- Workstation loyalty bonus

### Fixes
- Fixed HAR (Humanoid Alien Races) compatibility: Intelligence tab and comp now register on all humanlike races (reported by @Yaximbahps)
- Fixed hauling to full stockpile cells causing pawns to loop
- Fixed job report error with invalid {A} token
- Fixed zero-mass items blocking nearby item search
