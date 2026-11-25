# Server Configuration

## Install and upgrade

Use [Installation and Upgrade](Installation-and-Upgrade) for the full live-server and developer setup runbook. The most important paths are:

```text
addons/counterstrikesharp/plugins/WarcraftPlugin
addons/counterstrikesharp/configs/plugins/WarcraftPlugin/WarcraftPlugin.json
addons/counterstrikesharp/plugins/WarcraftPlugin/database.db
addons/counterstrikesharp/plugins/WarcraftPlugin/logs
```

## Default config values

| Key | Default | Meaning |
| --- | --- | --- |
| `ConfigVersion` | `12` | Current config schema version |
| `DeactivatedClasses` | `[]` | Races disabled at load time |
| `ShowCommandAdverts` | `true` | Teach players available commands in chat |
| `DefaultClass` | `ranger` | Starting race for new players |
| `DisableNamePrefix` | `false` | Show or hide the `[Level][Race]` name prefix |
| `XpPerKill` | `10` | Base kill XP |
| `XpHeadshotModifier` | `0.15` | Extra kill XP for headshots |
| `XpKnifeModifier` | `0.25` | Extra kill XP for knife kills |
| `XpPerRoundWin` | `30` | XP for winning a round |
| `XpPerAssist` | `5` | XP for assists |
| `XpPerBombPlant` | `15` | XP for planting |
| `XpPerBombDefuse` | `20` | XP for defusing |
| `EnableLevelDifferenceXp` | `true` | Bonus XP for killing higher-level players |
| `MatchReset` | `false` | Reset stored progression on map cycle |
| `SaveIntervalSeconds` | `120` | Dirty-player autosave interval; `0` disables periodic autosave |
| `MaxInventoryItems` | `2` | Persistent item carry limit |
| `EnableBotUltimates` | `true` | Allow bot ultimate controller |
| `DefaultMenuScale` | `Large` | Default Warcraft menu text scale: `Small`, `Normal`, `Large`, or `ExtraLarge` |
| `AllowPlayerMenuPreferences` | `true` | Allow players to save `!wcmenu` menu size/type preferences |
| `DefaultWarcraftMenuType` | `WasdMenu` | Default menu renderer: `WasdMenu`, `CenterHtmlMenu`, `ChatMenu`, or `ConsoleMenu` |
| `EnableDebugLogs` | `false` | Extra debug logging |

Warcraft cannot reliably detect a player's monitor resolution from the server. Players can use `!wcmenu`, `!warcraftmenu`, or `!menusize` to choose a readable menu size and renderer for their own setup.

## Logging and diagnostics

The plugin now writes persistent logs under:

```text
addons/counterstrikesharp/plugins/WarcraftPlugin/logs
```

Important files:

- `warcraft-YYYYMMDD.log` for the current day's persistent log stream
- `warcraft-breadcrumbs.log` for high-risk operation breadcrumbs
- `warcraft-last-breadcrumb.log` for the last recorded breadcrumb before a crash or shutdown

`EnableDebugLogs` controls extra verbose debug output from the plugin. It does not disable the persistent log files above. Those logs are intended to remain available for post-crash analysis even when normal debug spam is off.

If the server crashes and you do not get a managed stack trace, check `warcraft-last-breadcrumb.log` first. It is designed to show the last risky subsystem entered, such as native tracing, scripted damage, summon updates, or class-change flow.

## Default unlock thresholds

| Race key | Required total level |
| --- | ---: |
| `shadowblade` | `50` |
| `dwarf_engineer` | `60` |
| `death_weaver` | `70` |
| `silent_assassin` | `80` |
| `hammerstorm` | `90` |
| `sacred_warrior` | `100` |

## Item overrides

Items are configurable through `ItemOverrides`. Each item key is the normalized internal name, for example:

- `boots_of_speed`
- `tome_of_gambling`
- `weapon_insurance`
- `deathbound_bulwark`

Overrideable values depend on the item class, but common examples include:

- `Price`
- `IsDisabled`
- effect strength values such as damage reduction, XP gain, or grenade interval

Example:

```json
{
  "ItemOverrides": {
    "tome_of_gambling": {
      "Price": 6000,
      "MinXpGain": 75,
      "MaxXpGain": 200,
      "IsDisabled": false
    },
    "deathbound_bulwark": {
      "Price": 5000,
      "DamageReduction": 0.2
    }
  }
}
```

## Deactivating races

Races can be disabled by:

- class type name
- internal name
- display name

This means entries such as `Shadowblade`, `shadowblade`, or localized display names can all matter depending on your environment.

## Match reset behavior

If `MatchReset` is enabled:

- the plugin clears stored progression on map start
- the plugin clears stored progression on map end

If `MatchReset` is disabled:

- player data is saved on map end
- normal persistent race progression remains intact

## Name prefix behavior

When `DisableNamePrefix` is `false`, the plugin rewrites player names into this pattern:

```text
<level> [<race>] <real name>
```

This helps players read power levels in live rounds, but some servers may prefer to disable it for cleaner scoreboards.

## Bot behavior

- Bots receive a random race on join.
- Bots auto-spend skill points when they level up.
- Bot ultimates are controlled by `EnableBotUltimates`.

## API and package notes

The current project file references:

- `CounterStrikeSharp.API 1.0.381` (`net10.0`)
- `Dapper 2.1.100`
- `geometry3Sharp 1.0.324`
- `MIConvexHull 1.2.0`
- `Microsoft.CodeAnalysis.CSharp 5.12.0`
- `Microsoft.Data.Sqlite 11.0.0`
- `SQLite 3.54.1` (native engine)
- `MySqlConnector 3.0.0`
- `Tomlyn 0.19.0` (CounterStrikeSharp 1.0.381 supplies this assembly to the plugin at runtime. The newer serializer failed to load during Windows staging.)

If you upgrade CounterStrikeSharp, recheck this wiki against the live code before publishing it as authoritative documentation.
