# Developer Guide

This page explains how to change CS2 Warcraft without breaking event routing, player state, or effect cleanup.

## Code map

| Area | Responsibility |
| --- | --- |
| `WarcraftPlugin.cs` | Plugin lifecycle, config, commands, player load/save, class switching, timers, and top-level services. |
| `Core` | Class registration, XP, shop registry, player cache, cooldowns, SQLite persistence, bot ultimates, and weapon insurance. |
| `Events` | CounterStrikeSharp event bridge, Warcraft hurt/death wrappers, disconnect handling, and weapon-acquire hooks. |
| `Classes` | Built-in races and their abilities. |
| `Items` | Shop items, item config overrides, persistent item hooks, and instant item behavior. |
| `Models` | Runtime player and race models such as `WarcraftPlayer` and `WarcraftClass`. |
| `Helpers` | Shared safety, math, weapon, geometry, damage, entity, and utility helpers. |
| `Menu` | Warcraft menus for race selection, skills, shop, and command discovery. |
| `Diagnostics` | Persistent logs, breadcrumbs, and focused debug helpers. |
| `Compiler` | Runtime custom hero compilation and loading. |
| `CS2MenuManager` | Vendored menu library source. Keep local namespace/config behavior when syncing upstream fixes. |

## Main runtime flow

1. `Load()` initializes logging, config, XP, database, class registry, effects, cooldowns, bots, menus, commands, and event hooks.
2. A human client connect loads a `WarcraftPlayer` from SQLite and stores it in the plugin's `WarcraftPlayers` dictionary by controller handle.
3. Spawn handling calls race spawn logic, then applies default appearance, persistent item effects, and weapon insurance restore.
4. `EventSystem` routes game events into Warcraft race and item hooks.
5. XP and skill changes mark player progress dirty.
6. Autosave, round end, disconnect, class change, map end, and unload flush dirty player snapshots.
7. `Unload()` cleans hooks, timers, menus, effects, database workers, and helper systems.

## Adding or changing a race

- Derive from `WarcraftClass`.
- Keep `DisplayName`, `InternalName`, ability definitions, and localization keys stable unless you intend a migration.
- Cache ability lists instead of allocating a new list every access.
- Use `GetAbilityLevel(index)` and `IsAbilityReady(index)` for ability checks.
- Put spawn-only setup in spawn hooks, damage behavior in hurt hooks, cleanup in death/round/end/unload paths, and active ultimate behavior behind the ultimate command path.
- Use shared helpers such as `Warcraft.NextFrameIfAlive`, `Warcraft.CreateEntityByNameSafe`, `Geometry.AngleDeltaDegrees`, `WeaponTypes`, and `DamageMath` before adding new one-off logic.
- If the race creates effects, props, particles, timers, or hooks, define exactly which event destroys them: death, round end, map end, class change, unload, or timer expiry.
- Add unit tests for pure math, filtering, cooldown, progression, and persistence behavior. Use live-server validation for engine-only behavior.

## Adding or changing an item

- Derive from `ShopItem`.
- Keep the item name and normalized config key stable for existing server configs.
- Use `Apply()` for purchase/spawn application and event methods for gameplay behavior.
- Persistent items go into `WarcraftPlayer.Items`; instant items should complete their effect immediately and not occupy an inventory slot.
- Duplicate prevention belongs in item ownership rules, not in menu display text.
- If an item charges money before it finishes applying, failures must refund the player.
- Victim-side defensive items belong on victim hurt hooks. Attacker-side bonus damage, poison, slow, lifesteal, or money steal belongs on attacker hurt-other hooks.
- If multiple poison, slow, burn, or aura sources can coexist, key or refresh by source instead of blocking unrelated effects.

## Adding or changing an effect

- Prefer `Effect` subclasses for stateful gameplay that must tick or clean itself up.
- Register effects through `EffectManager` and choose clear cleanup flags.
- Make callbacks idempotent and safe when the owner, victim, pawn, weapon, prop, or particle is invalid.
- Stop effects after repeated callback failures instead of logging every tick forever.
- For bulk cleanup, assume map end, unload, and dispose should finish effects unless a specific effect is unsafe to finish.
- Avoid high-frequency timers when existing tick, drone, aura, or effect update loops can drive the behavior.

## Event routing rules

- `EventPlayerHurtOther` is attacker-side. Use it for outgoing bonus damage and on-hit attacker rewards.
- `EventPlayerHurt` is victim-side. Use it for evasion, shields, damage reduction, reflection, and death-prevention behavior.
- Lethal hits still need victim defensive hooks so effects can prevent or modify death.
- Do not keep references to short-lived game event objects for delayed callbacks. Capture stable identifiers or revalidate controllers and pawns later.
- Delayed work using `NextFrame`, `NextWorldUpdate`, or timers must re-check entity validity before touching native handles.
- Damage prevention should restore real pawn health and armor through the shared damage-cancellation pattern instead of only changing wrapper fields.

## Player state and persistence rules

- `WarcraftPlayer` is the live runtime state for one connected human player.
- Race XP, level, amount-to-level, and four ability levels are stored in SQLite.
- Persistent shop items persist across spawns while held in the live `WarcraftPlayer`; they are not currently stored in SQLite.
- Any code that changes XP, level, ability levels, or current race must mark progress dirty or use an existing save barrier.
- Class switching is a persistence-sensitive operation: save old race, clean active effects, write new current race, load target race, then swap the live object on the game thread.
- Disconnect and unload paths must not assume the player is alive or that an attacker exists.

## Config and localization

- Add config fields to `Config` with `JsonPropertyName`.
- Bump `ConfigVersion` only when server owners need regenerated config awareness or migration guidance.
- Document new config fields in `Server-Configuration.md`.
- Add language keys for player-visible text.
- Keep internal names and config keys lowercase and stable.

## Testing expectations

Before merging gameplay changes:

- Add pure unit tests for reusable helpers, progression, persistence coordination, item ownership, filtering, and damage math.
- Build Debug and Release.
- Run the test project.
- Run format verification.
- Check vulnerable and outdated packages.
- Run live CS2 validation for engine behavior, particles, props, hooks, menus, and damage routing.
