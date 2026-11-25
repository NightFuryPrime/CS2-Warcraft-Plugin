# Installation and Upgrade

This page is the operational runbook for installing, upgrading, rolling back, and building CS2 Warcraft.

## Requirements

- A working Counter-Strike 2 dedicated server.
- Metamod:Source 2.0 dev build `1467` or newer with KHook. Use build `1491` for this release's staging checks.
- CounterStrikeSharp `1.0.381` with its bundled .NET 10 runtime, matching the plugin's API package.
- File access to the CS2 server `game/csgo` directory.
- Git, if you are cloning the repository or building from source.

Warcraft `4.2.0` targets .NET 10. Install the `10.0.404` SDK or a later patch in the `10.0.4xx` feature band. You can build from Visual Studio 2026 with the SDK installed separately. Visual Studio 18.9 is the first release that bundled this SDK feature band.

Useful upstream references:

- [CounterStrikeSharp getting started](https://docs.cssharp.dev/docs/guides/getting-started.html)
- [CounterStrikeSharp hello world plugin guide](https://docs.cssharp.dev/docs/guides/hello-world-plugin.html)
- [CounterStrikeSharp releases](https://github.com/roflmuffin/CounterStrikeSharp/releases)
- [.NET 10 SDK downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Visual Studio and .NET SDK compatibility](https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs)
- [Metamod:Source dev builds](https://www.sourcemm.net/downloads.php?branch=master)
- [Metamod:Source documentation](https://wiki.alliedmods.net/Metamod:Source)
- [Git downloads](https://git-scm.com/downloads)
- [Installing Git](https://git-scm.com/book/en/v2/Getting-Started-Installing-Git)
- [SQLite backup documentation](https://sqlite.org/backup.html)

## Server paths

All paths below are relative to the CS2 server `game/csgo` folder.

| Purpose | Path |
| --- | --- |
| Plugin folder | `addons/counterstrikesharp/plugins/WarcraftPlugin` |
| Plugin DLL | `addons/counterstrikesharp/plugins/WarcraftPlugin/WarcraftPlugin.dll` |
| Generated config | `addons/counterstrikesharp/configs/plugins/WarcraftPlugin/WarcraftPlugin.json` |
| SQLite database | `addons/counterstrikesharp/plugins/WarcraftPlugin/database.db` |
| Persistent logs | `addons/counterstrikesharp/plugins/WarcraftPlugin/logs` |
| Language files | `addons/counterstrikesharp/plugins/WarcraftPlugin/lang` |
| Custom heroes | `addons/counterstrikesharp/plugins/WarcraftPlugin/CustomHeroes` |

## Install Metamod and CounterStrikeSharp

Warcraft is a CounterStrikeSharp plugin. CounterStrikeSharp itself is loaded by Metamod, so the dependency chain is:

```text
CS2 dedicated server
  -> Metamod:Source
    -> CounterStrikeSharp
      -> WarcraftPlugin
```

Use the official CounterStrikeSharp getting-started guide as the source of truth if these steps drift after a CS2 update.

### Metamod

1. Stop the CS2 server.
2. Download Metamod:Source 2.0 dev build `1491` for your server OS from [Metamod:Source dev builds](https://www.sourcemm.net/downloads.php?branch=master).
3. Extract the archive.
4. Copy the extracted `addons` folder into the server `game/csgo` folder, merging with any existing `addons` folder.
5. Open `game/csgo/gameinfo.gi`.
6. Add this line under `Game_LowViolence csgo_lv`:

```text
Game    csgo/addons/metamod
```

7. Start the server.
8. In the server console or RCON, run:

```text
meta version
meta list
```

If `meta list` is unknown, Metamod is not being loaded. Check the `addons` folder location and the `gameinfo.gi` edit. CS2 updates can replace `gameinfo.gi`, so re-check this file after game updates.

### CounterStrikeSharp

1. Stop the CS2 server after confirming Metamod loads.
2. Download the platform-specific `with-runtime` package from [CounterStrikeSharp 1.0.381](https://github.com/roflmuffin/CounterStrikeSharp/releases/tag/v1.0.381).
3. Include the matching .NET 10 runtime files when upgrading an existing installation.
4. Extract the archive.
5. Copy the extracted `addons` folder into the server `game/csgo` folder, merging with the existing Metamod `addons` folder.
6. Start the server.
7. In the server console or RCON, run:

```text
meta list
```

CounterStrikeSharp should appear as a loaded Metamod plugin. Then run:

```text
css_plugins list
```

If `css_plugins list` is unknown, CounterStrikeSharp did not load. On Windows servers, confirm the Visual C++ Redistributable requirement from the CounterStrikeSharp guide. On Linux servers, confirm the required ICU package is installed for .NET.

## Fresh install

1. Stop the CS2 server.
2. Install Metamod and CounterStrikeSharp according to the section above or the official CounterStrikeSharp guide.
3. Start the server once and run `meta list` from the server console or RCON. CounterStrikeSharp should appear as a loaded Metamod plugin.
4. Stop the server again.
5. Download or build the Warcraft release package.
6. Copy the complete `WarcraftPlugin` folder into `addons/counterstrikesharp/plugins/`.
7. Confirm that `WarcraftPlugin.dll` and dependency DLLs sit directly inside `addons/counterstrikesharp/plugins/WarcraftPlugin/`.
8. Start the server.
9. Confirm CounterStrikeSharp sees the plugin with `css_plugins list`.
10. Join the server and run `!class`, `!skills`, `!shop`, and `!commands`.
11. Bind an ultimate key, for example `bind x ultimate`, then test it after reaching the required skill state.

On first boot, the plugin creates `WarcraftPlugin.json`, `database.db`, and the `logs` folder automatically. Keep the generated config file and database out of release package replacements unless you intentionally want to reset server state.

## Install from Git/source

Use Git if you are deploying from the repository instead of a prepared release package.

Install Git first:

- Windows: download Git for Windows from [git-scm.com/downloads](https://git-scm.com/downloads).
- Linux: install through the distribution package manager, for example `sudo apt install git` on Debian or Ubuntu.

Clone and build:

```powershell
git clone https://github.com/Wngui/CS2WarcraftMod.git
cd CS2WarcraftMod
dotnet restore WarcraftPlugin\WarcraftPlugin.csproj
dotnet build WarcraftPlugin\WarcraftPlugin.csproj -c Release --no-incremental
```

For local server deployment from source, pass the deploy directory:

```powershell
dotnet build WarcraftPlugin\WarcraftPlugin.csproj -c Release -p:WarcraftPluginDeployDir="C:\Path\To\csgo\addons\counterstrikesharp\plugins\WarcraftPlugin"
```

Do not build directly over a live production server without taking a database and config backup first.

## Release package layout

A release folder should contain one top-level plugin directory:

```text
WarcraftPlugin/
  WarcraftPlugin.dll
  WarcraftPlugin.deps.json
  WarcraftPlugin.pdb
  *.dll Warcraft dependency files
  runtimes/
  lang/
    en.json
    da.json
    ru.json
    tr.json
```

Stage from clean Release output using the deploy target below. The CounterStrikeSharp installation supplies the API assembly. Back up the runtime database, generated config, logs, menu config, and local custom heroes before replacing the plugin folder.

## Upgrade

1. Announce downtime or move the server to an empty map.
2. Stop the CS2 server. A full restart is recommended for production upgrades.
3. Run the backup procedure below and keep the previous Metamod, CounterStrikeSharp, and Warcraft artifacts.
4. Upgrade to Metamod build `1491` and CounterStrikeSharp `1.0.381` with runtime. Recheck the Metamod entry in `gameinfo.gi` after updating CS2.
5. Move the previous Warcraft directory outside the server plugin search path, then install a clean Warcraft `4.2.0` deployment.
6. Copy the backed-up database, any matching SQLite sidecars, custom config, menu config, custom heroes, and logs to their original relative paths.
7. Start the server and check `meta version`, `meta list`, and `css_plugins list`.
8. Join as a real client and run the smoke test below.

CounterStrikeSharp can hot reload plugins when binaries are replaced, but production upgrades should use a full server restart. Hot reload is useful during development, not as the main upgrade path for a live server with persistent player data.

## Database backup

The live SQLite database is:

```text
addons/counterstrikesharp/plugins/WarcraftPlugin/database.db
```

The plugin enables SQLite WAL mode, so a running server may also have sidecar files:

```text
database.db-wal
database.db-shm
```

The safest backup is an offline backup while the CS2 server is stopped.

### Offline backup

1. Stop the CS2 server.
2. Create a timestamped backup folder outside the live plugin directory.
3. Copy the database, config, custom heroes, and optional logs.

PowerShell example:

```powershell
$csgo = "C:\Path\To\csgo"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = "C:\CS2Backups\WarcraftPlugin-$stamp"

New-Item -ItemType Directory -Force -Path $backup | Out-Null
Copy-Item "$csgo\addons\counterstrikesharp\plugins\WarcraftPlugin\database.db*" $backup -ErrorAction SilentlyContinue
Copy-Item "$csgo\addons\counterstrikesharp\configs\plugins\WarcraftPlugin\WarcraftPlugin.json" $backup -ErrorAction SilentlyContinue
Copy-Item "$csgo\addons\counterstrikesharp\plugins\WarcraftPlugin\config.toml" $backup -ErrorAction SilentlyContinue
Copy-Item "$csgo\addons\counterstrikesharp\plugins\WarcraftPlugin\CustomHeroes" "$backup\CustomHeroes" -Recurse -ErrorAction SilentlyContinue
Copy-Item "$csgo\addons\counterstrikesharp\plugins\WarcraftPlugin\logs" "$backup\logs" -Recurse -ErrorAction SilentlyContinue
```

Linux example:

```bash
CSGO="/path/to/csgo"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP="/srv/cs2-backups/WarcraftPlugin-$STAMP"

mkdir -p "$BACKUP"
cp "$CSGO/addons/counterstrikesharp/plugins/WarcraftPlugin"/database.db* "$BACKUP"/ 2>/dev/null || true
cp "$CSGO/addons/counterstrikesharp/configs/plugins/WarcraftPlugin/WarcraftPlugin.json" "$BACKUP"/ 2>/dev/null || true
cp "$CSGO/addons/counterstrikesharp/plugins/WarcraftPlugin/config.toml" "$BACKUP"/ 2>/dev/null || true
cp -a "$CSGO/addons/counterstrikesharp/plugins/WarcraftPlugin/CustomHeroes" "$BACKUP"/ 2>/dev/null || true
cp -a "$CSGO/addons/counterstrikesharp/plugins/WarcraftPlugin/logs" "$BACKUP"/ 2>/dev/null || true
```

### Online backup with SQLite

If you cannot stop the server, use the SQLite shell `.backup` command instead of copying only `database.db`. This asks SQLite to create a consistent backup from the live database.

```bash
sqlite3 "/path/to/csgo/addons/counterstrikesharp/plugins/WarcraftPlugin/database.db" ".backup '/srv/cs2-backups/WarcraftPlugin-live-backup.db'"
```

Offline backups are still preferred before upgrades, because they also capture matching config and custom hero files.

## Database restore

Restore only while the CS2 server is stopped.

1. Stop the CS2 server.
2. Back up the current live files before replacing them, even if you believe they are broken.
3. Remove the current live database files:

```text
database.db
database.db-wal
database.db-shm
```

4. Copy the backed-up `database.db` into `addons/counterstrikesharp/plugins/WarcraftPlugin/`.
5. If your backup contains `database.db-wal` and `database.db-shm` from an offline copy, restore those next to `database.db` as a matching set. Do not mix sidecar files from another backup.
6. Restore `WarcraftPlugin.json` only if you intentionally want the old config too.
7. Restore `CustomHeroes` if the database refers to custom races that must still exist.
8. Start the server.
9. Check `css_plugins list`, join, and verify race XP with `!class` and `!skills`.

If you have a single SQLite `.backup` output file, restore it as `database.db` and do not restore old `database.db-wal` or `database.db-shm` files.

## Rollback

1. Stop the server.
2. Restore the previous matched Metamod, CounterStrikeSharp, and Warcraft installations. Verify that the restored stack still supports the installed CS2 build before reopening the server.
3. Restore the matching backed-up `WarcraftPlugin.json` if config fields changed.
4. Restore the database only when needed for recovery. This discards progress made after the backup.
5. Start the server and verify with `css_plugins list`, `!class`, and `!skills`.

SQLite schema changes should be treated as rollback-sensitive. If a future release adds tables or columns, keep a database backup from before the upgrade.

## Smoke test

Run this after a fresh install, upgrade, or rollback:

1. Run `meta version` and `meta list` in the server console or RCON.
2. Server console or RCON: `css_plugins list`.
3. Client chat: `!commands`.
4. Client chat: `!class`, choose another race, then respawn or die to confirm the change path.
5. Client chat: `!skills`, spend a point if available.
6. Client chat: `!shop`, buy a cheap item if money allows.
7. Client console: `bind x ultimate`, then test the ultimate when available.
8. Restart the map or server and confirm the player's race XP still loads.
9. Check the Warcraft and CounterStrikeSharp logs. Warcraft startup should report SQLite `3.54.1`.
10. Repeat on Windows and Linux with a clean server and a copy of an existing database. Exercise an existing custom hero and menu config, scripted damage and kill attribution, drone line of sight, smoke, sounds, entity parenting, weapon pickup rules, invisibility, a map change, and plugin reload.

## Developer build and deploy

In Visual Studio 2026, open `WarcraftPlugin\WarcraftPlugin.sln`, select `Release`, and choose **Build Solution**. The plugin DLL is under `WarcraftPlugin\bin\Release\net10.0` relative to the repository root.

Build from the repository root:

```powershell
dotnet build WarcraftPlugin\WarcraftPlugin.csproj -c Debug --no-incremental
dotnet build WarcraftPlugin\WarcraftPlugin.csproj -c Release --no-incremental
```

Release builds create `WarcraftPlugin.4.2.0.nupkg` under the Release output directory. Use `WarcraftPluginDeployDir` to stage a server deployment from clean build output:

```powershell
dotnet build WarcraftPlugin\WarcraftPlugin.csproj -c Release --artifacts-path .artifacts -p:WarcraftPluginDeployDir="C:\Path\To\Staging\WarcraftPlugin"
```

Choose an empty artifacts and staging path for a clean release. To build HeroMaker and optionally copy its source files to a test server, run:

```powershell
dotnet build Tools\HeroMaker\HeroMaker.csproj -c Release
dotnet build Tools\HeroMaker\HeroMaker.csproj -c Release -p:CustomHeroDeployDir="C:\Path\To\WarcraftPlugin\CustomHeroes"
```

## Developer validation checklist

- Build Debug and Release.
- Run the test project.
- Run format verification.
- Check vulnerable and outdated packages.
- Hot reload twice on a test server.
- Change map once.
- Test a real player connect, disconnect, race change, death, spawn, shop purchase, item effect, and database reload.
- Run a bot round if the change touches targeting, auras, AoE, or bot ultimates.

## Common failures

| Symptom | Check |
| --- | --- |
| `meta list` is unknown | Metamod is not installed or `gameinfo.gi` is not loading it. |
| `css_plugins list` is unknown | CounterStrikeSharp is not loaded by Metamod. |
| Warcraft is not listed | Confirm the folder is `plugins/WarcraftPlugin/WarcraftPlugin.dll`, not nested one level deeper. |
| Config is missing | Start the server once; CounterStrikeSharp writes plugin config after load. |
| Commands do nothing | Check that the plugin loaded and that chat command prefixes are not blocked by another plugin. |
| Progress does not save | Check `database.db` permissions and `logs/warcraft-YYYYMMDD.log` for dirty-flush errors. |
| Crash after upgrade | Check CounterStrikeSharp runtime version, then inspect `warcraft-last-breadcrumb.log`. |
