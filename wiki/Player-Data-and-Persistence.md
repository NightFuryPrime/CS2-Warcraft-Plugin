# Player Data and Persistence

This page explains what player information exists in memory, what is saved to SQLite, and when it is written.

## In-memory state

The plugin keeps connected human players in a `ConcurrentDictionary<IntPtr, WarcraftPlayer>` named `WarcraftPlayers`. The key is the CounterStrikeSharp controller handle.

Each `WarcraftPlayer` holds:

- the current `CCSPlayerController`
- current race internal name
- current XP
- current level
- amount of XP needed for the next level
- four ability levels
- four ability cooldown values
- live persistent shop item objects
- the active `WarcraftClass` instance
- an optional desired class for queued class changes

This object is runtime state. It can contain native handles, live item instances, cooldown state, and race objects that only make sense while the player is connected.

## SQLite database

The database file is:

```text
addons/counterstrikesharp/plugins/WarcraftPlugin/database.db
```

The plugin uses SQLite with WAL mode, `synchronous=NORMAL`, and a busy timeout.

### `players`

One row per SteamID:

| Column | Meaning |
| --- | --- |
| `steamid` | Player SteamID and primary key. |
| `currentRace` | The race that should load when the player connects. |
| `name` | Last stored real player name. |

### `raceinformation`

One row per `steamid + racename`:

| Column | Meaning |
| --- | --- |
| `steamid` | Player SteamID. |
| `racename` | Race internal name. |
| `currentXP` | XP stored for that race. |
| `currentLevel` | Level stored for that race. |
| `amountToLevel` | Stored next-level XP value. Runtime recalculates this from the XP system when loading. |
| `ability1level` | Ability slot 1 level. |
| `ability2level` | Ability slot 2 level. |
| `ability3level` | Ability slot 3 level. |
| `ability4level` | Ultimate slot level. |

The primary key is `steamid + racename`, which is why each player can keep separate progress on multiple races.

## What is not stored

Persistent shop items are persistent across spawns while held in the live `WarcraftPlayer.Items` list. They are not currently written to SQLite.

Not stored in SQLite:

- persistent shop item inventory
- active cooldown timers
- active effects
- temporary props, particles, drones, traps, clones, or summons
- Weapon Insurance runtime token and pending restore state
- bot progress

If long-term item persistence is added later, it should use a separate table keyed by SteamID and stable item key, with migration and rollback notes in the release documentation.

## Load flow

When a human player connects:

1. The plugin checks for a valid non-bot controller.
2. If there is a dirty pending snapshot for the same SteamID, it flushes that snapshot first.
3. The `players` row is created if missing, using the configured default race.
4. If the stored race is disabled or missing, the player is moved back to the default race.
5. The `raceinformation` row for the active race is created if missing.
6. A new `WarcraftPlayer` is built and loaded from the database row.
7. The active `WarcraftClass` is instantiated and attached to the player.
8. The live player object is stored in `WarcraftPlayers`.

## Dirty snapshot flow

Player progress writes use a write-behind model:

1. Gameplay changes XP, level, ability levels, or current race.
2. Code calls `MarkPlayerProgressDirty(...)`.
3. `PlayerProgressSnapshot.TryCreate(...)` captures stable scalar values from the live player.
4. `PersistenceCoordinator` stores the newest dirty snapshot per SteamID and assigns a version.
5. Flush operations write snapshots through `SqliteDispatcher`.
6. A dirty entry is removed only if the flushed version is still the latest version for that SteamID.

This version check prevents an older flush from deleting a newer dirty update that arrived while the database worker was busy.

## SQLite dispatcher

`SqliteDispatcher` serializes database work onto one worker with one SQLite connection. This keeps database writes ordered and avoids doing SQLite work directly on hot gameplay paths.

The dispatcher exposes queue depth through `PendingQueueDepth`. Database flush logs include elapsed time, remaining dirty count, and queue depth so maintainers can spot persistence pressure.

## Save triggers

Progress can flush from these paths:

| Trigger | Behavior |
| --- | --- |
| Autosave | Flushes dirty players every `SaveIntervalSeconds`; default is `120`. |
| Round end | Queues a dirty-player flush after round XP and cleanup. |
| Disconnect | Captures a final snapshot and flushes that SteamID. |
| Class change | Uses a barrier save before switching away from the old race. |
| Map end | Flushes and drains dirty data when match reset is disabled. |
| Unload | Flushes and drains dirty data before database disposal. |
| Direct save barrier | Used when code needs a specific player saved before continuing. |

## Class changes

Class changes touch both runtime state and persistence:

1. Save the old race state.
2. Clean class effects and cooldown state.
3. Write the new `currentRace` to `players`.
4. Ensure the target `raceinformation` row exists.
5. Load the target race data.
6. Swap the live `WarcraftPlayer` on `Server.NextFrame()`.
7. Reapply preserved live persistent items if the player is alive.

If the target race cannot be loaded, the plugin tries to write the old race back to the database.

## Maintenance notes

- Back up `database.db` before upgrades.
- Do not edit the database while the server is running.
- Preserve `database.db` when replacing plugin binaries.
- Use `MatchReset` only when the server intentionally wants temporary progression.
- Check `warcraft-YYYYMMDD.log` for dirty flush timing and queue depth if XP or class changes appear delayed.
- Keep item persistence wording precise: current race progress is stored in SQLite; live persistent items are not.
