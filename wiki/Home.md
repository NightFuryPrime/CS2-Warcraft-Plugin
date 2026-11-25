# CS2 Warcraft Plugin Wiki

![Warcraft Mod Banner](https://github.com/user-attachments/assets/4f6fac3a-d098-4a41-8f46-2612e112529a)

This handbook documents the current code-truth behavior of the CS2 Warcraft plugin in this repository. It is written for three audiences at once:

- Players who want to understand how the mod plays.
- Server owners who need to configure and balance it.
- Coders who need a high-level behavior reference without digging through every class.

## At a glance

| System | Current value |
| --- | --- |
| Runtime version | `4.2.0` |
| CounterStrikeSharp API package | `1.0.381` (`net10.0`) |
| Config version | `12` |
| Races | `14` |
| Shop items | `18` |
| Max level per race | `16` |
| Ability layout | `3` passives + `1` ultimate |
| Max passive level | `5` |
| Max ultimate level | `1` |
| Default class | `Ranger` |
| Max persistent items carried | `2` |
| Autosave interval | `120` seconds |
| Persistence model | Separate progression per race |
| Database backend | SQLite via `database.db` |

## Start here

1. Pick a race with `!race`, `!class`, `!changerace`, or `!changeclass`.
2. Gain XP through kills, assists, bomb actions, and round wins.
3. Spend skill points with `!skills`.
4. Buy up to 2 persistent shop items with `!shop`.
5. Bind your ultimate with `bind x ultimate`.
6. Reach level 16 to unlock the final skill point for the ultimate slot.

## Handbook map

| Page | What it covers |
| --- | --- |
| [Installation and Upgrade](Installation-and-Upgrade) | Full install, upgrade, rollback, build, deploy, and smoke-test runbook |
| [Gameplay Flow](Gameplay-Flow) | Match loop, XP, leveling, class changes, and persistence |
| [Races and Abilities](Races-and-Abilities) | All 14 races, archetypes, unlock state, and real ability text |
| [Items and Shop](Items-and-Shop) | All 18 items, prices, instant vs persistent behavior |
| [Commands and Binds](Commands-and-Binds) | Player commands, admin commands, aliases, and recommended binds |
| [Server Configuration](Server-Configuration) | Default config values, unlock thresholds, overrides, and setup notes |
| [Technical Behavior](Technical-Behavior) | Spawn, death, round, bot, database, precache, and custom hero behavior |
| [Developer Guide](Developer-Guide) | Code structure, contribution recipes, event safety, effects, and testing |
| [Player Data and Persistence](Player-Data-and-Persistence) | In-memory player state, SQLite schema, dirty snapshots, and save triggers |
| [Maintenance Checklist](Maintenance-Checklist) | Release checklist, version bumps, validation commands, and rollback readiness |

## Core gameplay loop

CS2 Warcraft adds a persistent RPG layer on top of normal Counter-Strike rounds. Each player has a current race, separate stored progression for every race they have used, four skill slots, and optional shop items. The first three skill slots are standard abilities. The fourth slot is the ultimate and only becomes spendable once the race reaches max level.

Progression is race-specific. A player's Barbarian level does not replace their Mage level, and switching race loads that race's saved XP and ability levels from the database. Class changes can happen mid-session, but the active swap is applied safely on the game thread and is also handled on spawn if a change was queued while dead.

## Default progression and XP

### XP sources

| Action | Default XP |
| --- | --- |
| Kill | `10` |
| Assist | `5` |
| Bomb plant | `15` |
| Bomb defuse | `20` |
| Round win | `30` |
| Headshot bonus | `15%` of kill XP |
| Knife bonus | `25%` of kill XP |
| Level difference bonus | Enabled by default |

### XP curve

The plugin generates its level curve from:

- Initial XP requirement: `110`
- Multiplier per level: `1.07`
- Max level: `16`

## Default race unlocks

These unlock requirements are based on total combined levels across all races.

| Race | Default status |
| --- | --- |
| Barbarian | Unlocked by default |
| Necromancer | Unlocked by default |
| Paladin | Unlocked by default |
| Mage | Unlocked by default |
| Ranger | Unlocked by default |
| Rogue | Unlocked by default |
| Shapeshifter | Unlocked by default |
| Tinker | Unlocked by default |
| Shadowblade | Unlocks at total level `50` |
| Dwarf Engineer | Unlocks at total level `60` |
| Death Weaver | Unlocks at total level `70` |
| Silent Assassin | Unlocks at total level `80` |
| Hammerstorm | Unlocks at total level `90` |
| Sacred Warrior | Unlocks at total level `100` |

## Quick highlights

- Persistent races: each race stores its own XP, level, and four skill levels in SQLite.
- Cooldown ultimates: most ultimates are active abilities with cooldowns and require a bind.
- Spawn-driven setup: many passives apply on spawn, including bonus armor, health, decoys, molotovs, drones, and auras.
- Persistent items: non-instant items stay in the live Warcraft inventory and are re-applied on spawn. They are not currently stored in SQLite.
- Instant items: tomes and weapon insurance are consumed immediately and do not take inventory slots.
- Bot support: bots are assigned a random race and can auto-spend skill points; bot ultimates are enabled by default.
- Custom hero support: extra races can be loaded from `CustomHeroes/*.cs` and resource manifests.
- Persistent diagnostics: the plugin writes file-based logs and crash breadcrumbs under `WarcraftPlugin/logs` for post-crash investigation.

## Recommended first reads

- Start with [Gameplay Flow](Gameplay-Flow) if you are a player or server owner.
- Use [Installation and Upgrade](Installation-and-Upgrade) before deploying a live server.
- Jump to [Races and Abilities](Races-and-Abilities) if you are balancing the mod.
- Use [Technical Behavior](Technical-Behavior) if you are debugging spawn, death, class switching, or persistence.

## For developers

- Start with [Developer Guide](Developer-Guide) before changing races, items, effects, commands, or config.
- Use [Player Data and Persistence](Player-Data-and-Persistence) before touching XP, class switching, saves, or the SQLite schema.
- Follow [Maintenance Checklist](Maintenance-Checklist) before publishing a build or gameplay update.
