# Maintenance Checklist

Use this page before publishing a release or making gameplay changes.

## Version bump checklist

- Update `WarcraftPlugin.ModuleVersion`.
- Update `.csproj` `<Version>`.
- Update assembly version fields if present.
- Add a release section to `changelog.log`.
- Update the wiki version table in `Home.md`.
- Update package notes in `Server-Configuration.md`.
- Confirm `CounterStrikeSharp.API` package version matches the intended server runtime.
- Confirm `ConfigVersion` is bumped only when config shape or migration guidance changes.

## Required checks

Run from the repository root:

```powershell
dotnet build WarcraftPlugin\WarcraftPlugin.csproj -c Debug --no-incremental
dotnet build WarcraftPlugin\WarcraftPlugin.csproj -c Release --no-incremental
dotnet build Tools\HeroMaker\HeroMaker.csproj -c Release
dotnet test --project WarcraftPlugin.Tests\WarcraftPlugin.Tests.csproj
dotnet format WarcraftPlugin\WarcraftPlugin.csproj --verify-no-changes
dotnet list WarcraftPlugin\WarcraftPlugin.csproj package --vulnerable --include-transitive
dotnet list WarcraftPlugin\WarcraftPlugin.csproj package --outdated
```

Expected current notes:

- `CounterStrikeSharp.API` is pinned to `1.0.381` and requires the matching .NET 10 server runtime.
- Test the embedded menu TOML and a custom override under the CounterStrikeSharp host.
- Check the staged deployment for SQLite native assets on Windows and Linux and confirm the engine version in the Warcraft startup log.
- No vulnerable packages should be reported.

## Before changing gameplay

Identify these before editing:

- Which event path owns the behavior: spawn, hurt-other, hurt-victim, death, round, command, or timer.
- Whether the behavior modifies XP, level, ability levels, current race, or other saved state.
- Whether it creates effects, timers, props, particles, hooks, or user-message listeners.
- Which cleanup path owns the lifetime: death, class change, round end, map end, unload, dispose, or timer expiry.
- Whether the behavior needs config, localization, item override, or wiki updates.
- Whether pure logic can be covered by unit tests.

## Manual CS2 validation

Run these checks for gameplay releases:

- Hot reload twice on a test server.
- Full server restart.
- Map change.
- Human connect and disconnect.
- Race change while alive.
- Race change while dead.
- Spawn with persistent items.
- Death from player damage.
- Death from world or self damage.
- Shop purchase failure and duplicate item attempt.
- Ultimate bind and cooldown behavior.
- Menus: class, skills, shop, commands.
- Database persistence after unload or map end.
- Bot round if the change touches targeting, AoE, auras, or bot ultimates.

## Documentation checks

- Search wiki pages for stale versions before publishing.
- Keep install docs in `Installation-and-Upgrade.md`.
- Keep config defaults in `Server-Configuration.md`.
- Keep player storage details in `Player-Data-and-Persistence.md`.
- Keep coding recipes in `Developer-Guide.md`.
- Link every new wiki page from `_Sidebar.md` and `Home.md`.

## Rollback readiness

- Keep the previous release package.
- Keep a pre-upgrade copy of `WarcraftPlugin.json`.
- Keep a pre-upgrade copy of `database.db`.
- Note any database or config changes in `changelog.log`.
- If a release changes persistence shape, document whether older plugin versions can read the new data.
