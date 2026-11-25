# Commands and Binds

This page lists the current built-in commands registered by the plugin. Localized aliases can add more commands on top of these, but the aliases below are the canonical hard-coded ones from the current build.

## Recommended binds

```cfg
bind x ultimate
bind mouse4 ult
say !skills
say !shop
say !race
```

## Core player commands

| Purpose | Canonical aliases |
| --- | --- |
| Trigger ultimate | `ultimate`, `ult` |
| Open class menu | `changerace`, `changeclass`, `race`, `class`, `rpg`, `cr` |
| Reset current race skill points | `reset`, `resetskills`, `resetskillpoints` |
| Fully reset current race progression | `factoryreset`, `fullreset`, `resetlevels` |
| Open skills menu | `skills`, `level` |
| Open shop | `shopmenu`, `shop`, `buymenu` |
| Configure menu display | `wcmenu`, `warcraftmenu`, `menusize` |
| Show Warcraft info | `info`, `wcinfo` |
| Show help | `rpg_help`, `commands`, `wcs`, `war3menu` |

## Admin and diagnostics commands

| Purpose | Canonical aliases | Notes |
| --- | --- | --- |
| Add XP | `addxp`, `givexp`, `xpadd` | Requires `@css/addxp` |
| Cooldown diagnostics | `cooldown.test`, `testcooldowns` | Requires `@css/root` |
| Performance capture | `perf.capture [seconds]` | Requires `@css/root`; captures 10-300 seconds, default 60 |

## Command behavior notes

### `!race` / `!class`

Opens the race menu and shows:

- current race level
- unlock status
- total-level requirements for locked races

### `!skills`

Opens the skill-spending menu for the current race. The ultimate slot stays locked until the race reaches level `16`.

### `!wcmenu`

Opens Warcraft menu display settings. Players can choose menu text size and renderer type when the default menu is too small for their resolution or HUD scale. The server cannot reliably auto-detect client monitor resolution, so these settings are player-selected and persisted.

### `!info`

Prints:

- target race
- target level and XP state
- all four ability levels
- current buffs such as speed and gravity modifiers

### `!factoryreset`

Resets only the current race:

- level to `0`
- XP to `0`
- ability points to `0`

The command does not wipe every race on the account.

### `!addxp <amount> [target]`

Adds XP to the caller or to an explicit target if one is provided.

Examples:

```cfg
css_addxp 50
css_addxp 200 SomePlayer
```

Depending on how your server exposes chat commands, admins may use either chat-triggered or console-triggered command forms.

### `!perf.capture [seconds]`

Runs an opt-in performance capture for 10–300 seconds (60 by default). The completion report is written to the persistent Warcraft log and includes min/average/p95/max timings for Warcraft event and tick paths, active effects, drones, invisibility entries, player count, and SQLite queue/dirty-player depth. No samples are stored while a capture is inactive.

## Help and discoverability

The plugin can also advertise commands automatically in chat. This is controlled by the `ShowCommandAdverts` config option, which defaults to `true`.

The Warcraft menu setting now controls the actual renderer used by `!class`, `!skills`, `!shop`, and `!wcmenu`. Select `ConsoleMenu` in `!wcmenu` when isolating a suspected center-HTML/WASD rendering issue.
