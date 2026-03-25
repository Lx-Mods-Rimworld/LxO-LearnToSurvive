# LxO - Learn to Survive

A RimWorld mod that adds **five learnable intelligence stats** to your colonists. They start dumb (vanilla behavior) and get smarter through experience. Skills never decay. Veteran colonists become irreplaceable.

## The Concept

Every RimWorld player knows the pain: colonists haul one item at a time, eat raw food next to a stove, walk through bedrooms, stand in fire during combat, and ignore nearby work to walk across the map.

**Learn to Survive** turns that frustration into a progression system. Level 0 is vanilla. Each level adds one specific improvement. Pawns learn by doing. A colonist who hauls for a season becomes an efficient logistics expert. A soldier who survives ten raids learns to use cover and avoid friendly fire.

Losing a veteran hurts -- because you lose years of learned intelligence.

## The Five Stats

### Hauling Sense (0-20)
How efficiently a colonist moves items around the colony.
- **Lv 1-3:** Walks to nearby stacks and merges them into one carry
- **Lv 4:** Inventory hauling -- carries multiple items per trip
- **Lv 5-7:** Prefers closer jobs, prioritizes perishables
- **Lv 8-11:** Batches by destination, avoids duplicate hauling
- **Lv 12+:** Opportunistic hauling, construction staging, hauler coordination
- **Lv 20:** Mentors nearby pawns (+25% hauling XP)

### Work Awareness (0-20)
How intelligently a colonist selects, prepares for, and chains tasks.
- **Lv 1-3:** Checks nearby for work before walking across the map
- **Lv 4-5:** Cleans the kitchen before cooking
- **Lv 6-7:** Gathers all ingredients in one trip, carries full stacks to construction
- **Lv 8:** Chains deconstruct/harvest/mine jobs before hauling results
- **Lv 9-12:** Checks needs before long tasks, remembers preferred workstation
- **Lv 13+:** Resource awareness, bottleneck relief, anticipatory crafting
- **Lv 20:** Mentors nearby pawns (+25% work XP, +8% workstation speed)

### Path Memory (0-20)
How efficiently a colonist navigates the colony.
- **Lv 1-4:** Prefers familiar paths, discovers shortcuts
- **Lv 5-7:** Avoids walking through bedrooms, hospitals, prisons
- **Lv 8:** Remembers where they were injured -- avoids danger zones
- **Lv 9-11:** Prefers constructed floors, avoids mud
- **Lv 12:** Prefers roofed paths during rain, snow, toxic fallout
- **Lv 16+:** Shares path knowledge with nearby colonists
- **Lv 20:** Navigation mentor -- teaches all colonists on the map

### Combat Instinct (0-20)
How a colonist behaves during threats.
- **Lv 1-2:** Moves out of fire and gas faster
- **Lv 3-4:** Seeks cover when under fire
- **Lv 5-7:** Friendly fire prevention -- holds fire when ally is in the way
- **Lv 8:** Target prioritization -- breachers and chargers first
- **Lv 9-12:** Wounded retreat, auto-undraft at critical health
- **Lv 13+:** AoE spacing, role positioning, focus fire
- **Lv 20:** Combat commander -- nearby pawns gain +3 Combat Instinct

### Self-Preservation (0-20)
How well a colonist manages their own needs and health.
- **Lv 1-3:** Prefers cooked food, seeks quality meals
- **Lv 4:** Plans meals near tables -- drastically reduces "ate without table"
- **Lv 5-6:** Eats before starving, sleeps before exhaustion
- **Lv 8:** Need forecasting -- plans activity sequences for multiple needs
- **Lv 12:** Medicine matching -- herbal for cuts, glitterworld for emergencies
- **Lv 16:** Proactive mood management near mental break threshold
- **Lv 20:** Calming presence -- nearby pawns gain +3 mood

## Books & Teaching

Colonists who reach **level 20** in any stat can write educational books at a Writing Spot. Other colonists read these books during recreation time, gaining both joy and intelligence XP.

- Books are named after their author and topic
- Quality depends on the author's Intellectual skill
- Diminishing returns on re-reading the same book
- Pawns prefer books for stats they're weakest in

## Learning System

- **Learn by doing.** Haul items to improve Hauling Sense. Survive raids to improve Combat Instinct. Walk around to build Path Memory.
- **Never forget.** Intelligence stats don't decay. Ever.
- **Backstory matters.** Glitterworld spacers start smarter than tribals. Military backgrounds give combat bonuses.
- **Traits affect speed.** Industrious pawns learn Work Awareness faster. Joggers learn Path Memory faster. Tough pawns learn Combat Instinct faster.
- **Mentoring.** Level 20 masters boost XP gain for nearby pawns by 25%.
- **Tunable.** XP rate multiplier in settings. Each stat can be individually enabled/disabled.

## Compatibility

- **Pick Up And Haul** -- works alongside. Toggle in settings to let PUAH handle hauling.
- **Common Sense** -- works alongside. Toggle in settings to let CS handle pre-cleaning.
- **Combat Extended** -- detected and respected.
- **Modded races** -- intelligence stats are added to all humanlike pawns automatically.
- Safe to add mid-save. Colonists start at backstory-appropriate levels.

## Decision Logging

Every AI decision can be logged for debugging and tuning:
- **Off** -- no logging (default)
- **Summary** -- level-ups only
- **Decisions** -- every AI choice with context and reasoning
- **Verbose** -- XP ticks, cache stats, full detail

Logs write to `ColonistAI_Decisions.log` in your RimWorld save folder with automatic rotation at 10MB.

## Requirements

- RimWorld 1.6+
- [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077)

## Installation

Subscribe on Steam Workshop, or download and extract to your RimWorld `Mods` folder.

## Credits

Developed by **Lexxers** ([Lx-Mods-Rimworld](https://github.com/Lx-Mods-Rimworld))

Free forever. If you enjoy this mod, consider supporting development:
**[Ko-fi](https://ko-fi.com/lexxers)**

## License

- **Code:** MIT License
- **Content (textures, XML):** CC-BY-SA 4.0
