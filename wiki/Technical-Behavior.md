# Technical Behavior

This page is for server owners and coders who need to understand how the plugin behaves at runtime without reading every implementation file.

For deeper implementation notes, use [Developer Guide](Developer-Guide). For saved player data, use [Player Data and Persistence](Player-Data-and-Persistence).

## Lifecycle overview

### Load

On load, the plugin:

1. Loads localization files.
2. Generates the XP curve.
3. Initializes the SQLite database.
4. Registers built-in races and any custom hero classes.
5. Initializes effect, cooldown, bot-ultimate, and advert systems.
6. Registers commands, listeners, event hooks, and the weapon-acquire virtual hook.
7. Starts a repeating autosave timer.

### Unload

On unload, the plugin:

- releases active menus by restoring movement
- disposes the event system
- stops bot and advert helpers
- kills the autosave timer
- saves clients
- disposes the database
- unloads the volume fix helper
- clears bulk effect and silent-footstep state

## Spawn behavior

Player spawn is split into two phases:

1. The race gets its spawn event and can run race-specific logic.
2. Once the pawn is ready, the plugin applies default appearance, persistent items, and weapon insurance restore logic.

This matters because several race effects need a valid pawn body or skeleton before they can safely apply models, scale, health, armor, or movement state.

## Round behavior

### Round start

At round start, the plugin:

- resets per-race cooldowns on the next frame
- prints round info or opens the skills menu if points are available
- prints owned persistent items
- optionally sends ability benefit summaries

### Round end

At round end, the plugin:

- destroys effects flagged for round-end cleanup
- awards winning-team round XP to non-bot human players
- saves player progress for valid non-bot players

## Damage and death behavior

### Hurt pipeline

The event system turns `EventPlayerHurt` into two Warcraft-facing paths:

- attacker-side `EventPlayerHurtOther`
- victim-side `EventPlayerHurt`

This lets races and items participate in:

- bonus damage
- on-hit poison or slow
- evasion
- reflection
- armor stripping
- other hurt-time passives

### Death pipeline

On death, the plugin:

- awards kill XP
- awards assist XP
- lets race-specific kill and death handlers run
- destroys effects flagged for death cleanup
- snapshots Weapon Insurance loadouts
- processes queued class changes if needed

## Class switching behavior

Class changes are designed to keep per-race persistence safe.

The current implementation:

1. Saves the old race state.
2. Clears active race effects and cooldown state.
3. Saves the new current race name explicitly in the database.
4. Reloads the destination race from the database.
5. Swaps the live Warcraft player object on `Server.NextFrame()`.
6. Reapplies preserved persistent items if the player is alive.

If the destination race cannot be reloaded, the plugin tries to revert the active race in the database.

## Persistence details

SQLite tables:

- `players`
  - stores `steamid`, `currentRace`, and last stored `name`
- `raceinformation`
  - stores XP, level, amount-to-level, and four ability levels per `steamid + racename`

Autosave behavior:

- repeating save timer every `120` seconds by default
- round-end save
- map-end save when match reset is disabled
- disconnect save

The full write-behind model is documented in [Player Data and Persistence](Player-Data-and-Persistence).

## Item behavior internals

- Persistent items live in `WarcraftPlayer.Items` while the player is connected.
- Instant items apply immediately and are not stored.
- `ApplyItems()` re-runs the persistent item effects on spawn.
- Item hooks can respond to attacker-side and victim-side hurt events.

Persistent shop items are not currently stored in SQLite. Weapon Insurance is separate from the normal persistent inventory. It tracks a one-shot token and stores purchased weapon names between death and next spawn.

## Bot behavior

- Bots are created with a random race from the currently registered race list.
- Bot race progress starts fresh in memory.
- Bots auto-spend skill points on level-up.
- Bot ultimates are managed by a dedicated controller if enabled in config.

## Precache and resources

The plugin precaches:

- core shared models and sounds
- particle resources
- per-race preload resources
- custom hero resource manifests

This is why some races declare `PreloadResources`, especially when they spawn custom props, projectiles, or models.

## Custom heroes

Custom heroes are loaded from:

```text
WarcraftPlugin/CustomHeroes/*.cs
```

Behavior:

- custom source files are compiled and loaded at runtime
- the plugin watches for file timestamp changes
- a change triggers a plugin reload by touching the DLL timestamp
- optional resource manifests can be provided through `.resources` files or `resources.txt`

## Event-safety notes

- Game events are handled within the event system and race event bindings.
- Delayed spawn follow-up work is routed through safe non-event entry points instead of reusing expired game event instances.
- Native function hooks that are not managed by `BasePlugin` are explicitly cleaned up on dispose.

## Debugging tips

If behavior looks wrong on a live server, check these areas first:

1. `WarcraftPlugin.json` for disabled races, altered thresholds, or item overrides.
2. `database.db` for stale or unexpected stored race progression.
3. class-specific spawn code for effects that require a ready pawn.
4. hot reload paths if behavior duplicates after plugin reloads.

## Logging and crash forensics

The plugin keeps persistent diagnostics on disk so crash investigation does not depend entirely on live console output.

Default log folder:

```text
addons/counterstrikesharp/plugins/WarcraftPlugin/logs
```

Files:

- `warcraft-YYYYMMDD.log`
  - daily persistent log output
- `warcraft-breadcrumbs.log`
  - rolling breadcrumb history for risky execution paths
- `warcraft-last-breadcrumb.log`
  - last recorded breadcrumb, useful when the server process dies before normal exception logging

The breadcrumb system is especially useful for server-level crashes because native engine faults often do not produce a managed plugin exception. The plugin writes breadcrumbs before high-risk areas such as:

- native ray tracing
- scripted damage application
- summon activation and updates
- spawn-time class transition flow

Recommended crash workflow:

1. Reproduce the crash once.
2. Open `warcraft-last-breadcrumb.log` first.
3. Match that breadcrumb with the surrounding entries in the current daily log.
4. Narrow the issue to the responsible subsystem before testing code changes.

This does not guarantee a full stack trace for an engine crash, but it significantly improves the odds of finding the last plugin-controlled operation before the process died.
