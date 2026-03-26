# Changelog

All notable changes to this mod will be documented in this file.

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
