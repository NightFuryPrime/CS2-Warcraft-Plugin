# Gameplay Flow

## What a normal match looks like

1. A player joins and is assigned the configured default class if they do not exist in the database yet.
2. The plugin loads that player's saved progression for the current race from SQLite.
3. On spawn, the current race applies its spawn logic and the player's persistent items are re-applied.
4. During the round, the player earns XP from kills, assists, bomb plants, bomb defuses, and round wins.
5. When a level is earned, the player gets another skill point. At level 16, the final point can be spent into the ultimate slot.
6. On round end and on timed autosave, progression is written back to the database.

## Race selection and class changes

Players can open the class menu with:

- `!race`
- `!class`
- `!changerace`
- `!changeclass`
- `!rpg`
- `!cr`

Class changes are race-specific persistence swaps, not a full character wipe. The system:

1. Saves the current race state.
2. Cleans up temporary class effects and cooldown state.
3. Writes the destination race as the active race in the player table.
4. Loads the destination race progress.
5. Replaces the in-memory player state on `Server.NextFrame()`.

If a player queues a race change while dead, the change is applied on spawn.

## XP and leveling behavior

### Base XP sources

| Event | Default value |
| --- | --- |
| Kill | `10 XP` |
| Assist | `5 XP` |
| Bomb plant | `15 XP` |
| Bomb defuse | `20 XP` |
| Round win | `30 XP` |

### Kill modifiers

| Modifier | Default |
| --- | --- |
| Headshot bonus | `15%` of base kill XP |
| Knife bonus | `25%` of base kill XP |
| Level-difference scaling | Enabled |

If level-difference XP is enabled, killing a higher-level player grants extra XP based on the gap between attacker level and victim level.

## Skill point rules

- Each race has four ability slots.
- Slots `1-3` are normal abilities and can each reach level `5`.
- Slot `4` is the ultimate and can only reach level `1`.
- The ultimate slot is only eligible once the race reaches level `16`.
- Bots auto-spend available skill points after leveling.
- Human players use the skill menu with `!skills`.

## Ultimate behavior

Ultimates are usually cooldown abilities and are not mapped to a weapon by default. Players should bind them manually, for example:

```cfg
bind x ultimate
bind mouse4 ult
```

The plugin blocks ultimate use while dead.

## Shop behavior

- Open the shop with `!shop`, `!shopmenu`, or `!buymenu`.
- Persistent items occupy live inventory slots and the default carry limit is `2`.
- Duplicate persistent items are blocked.
- Instant items do not take inventory slots.
- Persistent items are re-applied when the player spawns again.

## Persistence model

The plugin stores:

- Current active race per player in the `players` table.
- Per-race XP, level, and ability levels in the `raceinformation` table.

This means:

- A player can have progress on multiple races.
- Switching race loads that race's saved state instead of resetting it.
- Shop items persist across spawns only while held in the live `WarcraftPlayer`; they are not currently stored in SQLite.
- Match resets can wipe stored progression if `MatchReset` is enabled.

## Round behavior summary

### Round start

- Cooldowns are reset.
- If the player has free skill points, the skill menu can open automatically.
- Otherwise the player gets a short center-screen summary of class and XP.
- Owned persistent items are printed in chat.

### Round end

- End-of-round effects are cleaned up.
- Winning human players get round-win XP.
- Dirty player progress is queued for database flush.

### Death

- Death-triggered effects are cleaned up.
- Kill XP and assist XP are processed.
- Weapon Insurance snapshots purchased weapons for the next spawn if active.
- A queued class change can also finish on death.
