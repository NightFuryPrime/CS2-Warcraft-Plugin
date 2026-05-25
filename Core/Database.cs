using CounterStrikeSharp.API.Core;
using Dapper;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WarcraftPlugin.Diagnostics;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Core
{
    internal class Database : IDisposable
    {
        private const string PlayerUpsertSql = @"
            INSERT INTO `players` (`steamid`, `currentRace`, `name`)
            VALUES (@steamid, @currentRace, @name)
            ON CONFLICT(`steamid`) DO UPDATE SET
                `currentRace` = excluded.`currentRace`,
                `name` = excluded.`name`;";

        private const string RaceUpsertSql = @"
            INSERT INTO `raceinformation`
                (`steamid`, `racename`, `currentXP`, `currentLevel`, `amountToLevel`,
                 `ability1level`, `ability2level`, `ability3level`, `ability4level`)
            VALUES
                (@steamid, @racename, @currentXp, @currentLevel, @amountToLevel,
                 @ability1Level, @ability2Level, @ability3Level, @ability4Level)
            ON CONFLICT(`steamid`, `racename`) DO UPDATE SET
                `currentXP` = excluded.`currentXP`,
                `currentLevel` = excluded.`currentLevel`,
                `amountToLevel` = excluded.`amountToLevel`,
                `ability1level` = excluded.`ability1Level`,
                `ability2level` = excluded.`ability2Level`,
                `ability3level` = excluded.`ability3Level`,
                `ability4level` = excluded.`ability4Level`;";

        private SqliteDispatcher _dispatcher;
        private readonly PersistenceCoordinator _persistence = new();
        private bool _disposed;
        private string _databasePath = string.Empty;

        internal int DirtyCount => _persistence.DirtyCount;
        internal int PendingQueueDepth => _dispatcher?.PendingCount ?? 0;

        internal void Initialize(string directory)
        {
            _databasePath = Path.Join(directory, "database.db");
            _dispatcher = new SqliteDispatcher($"Data Source={_databasePath}");
            _dispatcher.ExecuteAsync("database-init", InitializeSchema).GetAwaiter().GetResult();
            PersistentLogger.Info(nameof(Database), $"Initialized SQLite dispatcher for '{_databasePath}'.", mirrorConsole: true);
        }

        internal void MarkPlayerDirty(CCSPlayerController player, string reason = "progress")
        {
            if (!PlayerProgressSnapshot.TryCreate(player, out var snapshot))
                return;

            MarkPlayerDirty(snapshot, reason);
        }

        internal void MarkPlayerDirty(PlayerProgressSnapshot snapshot, string reason = "progress")
        {
            _persistence.MarkDirty(snapshot);

            if (WarcraftPlugin.Instance.Config?.EnableDebugLogs == true)
            {
                WarcraftPlugin.Instance.DebugLog($"Marked player {snapshot.SteamId} dirty for '{reason}'. dirtyCount={DirtyCount}");
            }
        }

        internal async Task<WarcraftPlayer> LoadPlayerFromDatabase(CCSPlayerController player, XpSystem xpSystem)
        {
            if (player == null || !player.IsValid || player.IsBot)
                return null;

            return await LoadPlayerFromDatabase(
                player,
                xpSystem,
                (long)player.SteamID,
                player.GetRealPlayerName(),
                notifyDisabledClass: true);
        }

        internal async Task<WarcraftPlayer> LoadPlayerFromDatabase(
            CCSPlayerController player,
            XpSystem xpSystem,
            long steamId,
            string playerName,
            bool notifyDisabledClass = false)
        {
            if (player == null || steamId <= 0)
                return null;

            var defaultClass = WarcraftPlugin.Instance.classManager.GetDefaultClass();
            var defaultRace = defaultClass.InternalName;
            var initialAmountToLevel = xpSystem.GetXpForLevel(0);

            if (_persistence.TryGetDirty(steamId, out var pendingDirty))
            {
                await FlushBatchAsync([pendingDirty], "connect-reconcile");
            }

            var dbPlayer = await EnsurePlayerRecordAsync(steamId, playerName, defaultRace);
            if (dbPlayer == null)
            {
                PersistentLogger.Error(nameof(LoadPlayerFromDatabase), $"Failed to load or create player row for steamid={steamId}.");
                return null;
            }

            var currentRace = dbPlayer.CurrentRace;
            if (!WarcraftPlugin.Instance.classManager.GetAllClasses().Any(x => x.InternalName == currentRace))
            {
                currentRace = defaultRace;
                if (notifyDisabledClass)
                {
                    CounterStrikeSharp.API.Server.NextFrame(() =>
                    {
                        if (player.IsValid)
                            player.PrintToChat(" " + WarcraftPlugin.Instance.Localizer["class.disabled", defaultClass.LocalizedDisplayName]);
                    });
                }

                await SaveCurrentClassAsync(steamId, playerName, currentRace, initialAmountToLevel);
            }

            var raceInformation = await EnsureRaceInformationAsync(steamId, currentRace, initialAmountToLevel);
            if (raceInformation == null)
            {
                PersistentLogger.Error(nameof(LoadPlayerFromDatabase), $"Failed to load race information for steamid={steamId}, race='{currentRace}'.");
                return null;
            }

            var wcPlayer = new WarcraftPlayer(player);
            wcPlayer.LoadClassInformation(raceInformation, xpSystem);
            return wcPlayer;
        }

        internal async Task<List<ClassInformation>> LoadClassInformationFromDatabase(CCSPlayerController player)
        {
            if (player == null || !player.IsValid || player.IsBot)
                return [];

            var steamId = (long)player.SteamID;
            if (_persistence.TryGetDirty(steamId, out _))
            {
                await SavePlayerToDatabase(player);
            }

            return await _dispatcher.ExecuteAsync("load-class-information", connection =>
            {
                return connection.Query<ClassInformation>(@"
                    SELECT *
                    FROM `raceinformation`
                    WHERE `steamid` = @steamid;",
                    new { steamid = steamId }).AsList();
            });
        }

        internal Task SavePlayerToDatabase(CCSPlayerController player)
        {
            if (!PlayerProgressSnapshot.TryCreate(player, out var snapshot))
                return Task.CompletedTask;

            var dirtyEntry = _persistence.MarkDirty(snapshot);
            return FlushBatchAsync([dirtyEntry], "player-barrier");
        }

        internal Task FlushDirtyPlayerAsync(long steamId, string reason)
        {
            if (!_persistence.TryGetDirty(steamId, out var dirtyEntry))
                return Task.CompletedTask;

            return FlushBatchAsync([dirtyEntry], reason);
        }

        internal Task FlushDirtyPlayersAsync(string reason)
        {
            return FlushBatchAsync(_persistence.SnapshotDirtyPlayers(), reason);
        }

        internal async Task FlushAllDirtyAndDrainAsync(string reason, TimeSpan timeout)
        {
            await FlushDirtyPlayersAsync(reason).WaitAsync(timeout);
            await _dispatcher.DrainAsync(reason, timeout);
        }

        internal Task SaveClients()
        {
            return FlushDirtyPlayersAsync("save-clients");
        }

        internal Task SaveCurrentClass(CCSPlayerController player, string className)
        {
            if (player == null || !player.IsValid || player.IsBot || string.IsNullOrWhiteSpace(className))
                return Task.CompletedTask;

            return SaveCurrentClass((long)player.SteamID, player.GetRealPlayerName(), className);
        }

        internal Task SaveCurrentClass(long steamId, string playerName, string className)
        {
            if (steamId <= 0 || string.IsNullOrWhiteSpace(className))
                return Task.CompletedTask;

            return SaveCurrentClassAsync(
                steamId: steamId,
                playerName: playerName ?? string.Empty,
                className: className,
                initialAmountToLevel: GetInitialAmountToLevel());
        }

        internal void ResetClients()
        {
            _persistence.ClearAll();

            _dispatcher.ExecuteAsync("reset-clients", connection =>
            {
                using var transaction = connection.BeginTransaction();
                connection.Execute("DELETE FROM `players`;", transaction: transaction);
                connection.Execute("DELETE FROM `raceinformation`;", transaction: transaction);
                transaction.Commit();
            }).GetAwaiter().GetResult();
        }

        private async Task<DatabasePlayer> EnsurePlayerRecordAsync(long steamId, string playerName, string defaultRace)
        {
            return await _dispatcher.ExecuteAsync("ensure-player-record", connection =>
            {
                connection.Execute(@"
                    INSERT INTO `players` (`steamid`, `currentRace`, `name`)
                    VALUES (@steamid, @currentRace, @name)
                    ON CONFLICT(`steamid`) DO NOTHING;",
                    new
                    {
                        steamid = steamId,
                        currentRace = defaultRace,
                        name = playerName
                    });

                return connection.QueryFirstOrDefault<DatabasePlayer>(@"
                    SELECT *
                    FROM `players`
                    WHERE `steamid` = @steamid;",
                    new { steamid = steamId });
            });
        }

        private async Task<ClassInformation> EnsureRaceInformationAsync(long steamId, string raceName, int initialAmountToLevel)
        {
            return await _dispatcher.ExecuteAsync("ensure-race-information", connection =>
            {
                connection.Execute(@"
                    INSERT INTO `raceinformation`
                        (`steamid`, `racename`, `currentXP`, `currentLevel`, `amountToLevel`,
                         `ability1level`, `ability2level`, `ability3level`, `ability4level`)
                    VALUES
                        (@steamid, @racename, 0, 0, @amountToLevel, 0, 0, 0, 0)
                    ON CONFLICT(`steamid`, `racename`) DO NOTHING;",
                    new
                    {
                        steamid = steamId,
                        racename = raceName,
                        amountToLevel = initialAmountToLevel
                    });

                return connection.QueryFirstOrDefault<ClassInformation>(@"
                    SELECT *
                    FROM `raceinformation`
                    WHERE `steamid` = @steamid AND `racename` = @racename;",
                    new
                    {
                        steamid = steamId,
                        racename = raceName
                    });
            });
        }

        private Task SaveCurrentClassAsync(long steamId, string playerName, string className, int initialAmountToLevel)
        {
            return _dispatcher.ExecuteAsync("save-current-class", connection =>
            {
                using var transaction = connection.BeginTransaction();

                connection.Execute(PlayerUpsertSql,
                    new
                    {
                        steamid = steamId,
                        currentRace = className,
                        name = playerName
                    },
                    transaction);

                connection.Execute(@"
                    INSERT INTO `raceinformation`
                        (`steamid`, `racename`, `currentXP`, `currentLevel`, `amountToLevel`,
                         `ability1level`, `ability2level`, `ability3level`, `ability4level`)
                    VALUES
                        (@steamid, @racename, 0, 0, @amountToLevel, 0, 0, 0, 0)
                    ON CONFLICT(`steamid`, `racename`) DO NOTHING;",
                    new
                    {
                        steamid = steamId,
                        racename = className,
                        amountToLevel = initialAmountToLevel
                    },
                    transaction);

                transaction.Commit();
            });
        }

        private async Task FlushBatchAsync(IReadOnlyList<DirtyPlayerProgress> dirtyPlayers, string reason)
        {
            if (dirtyPlayers == null || dirtyPlayers.Count == 0)
                return;

            var timer = Stopwatch.StartNew();
            await _dispatcher.ExecuteAsync($"flush:{reason}", connection => SaveSnapshotBatch(connection, dirtyPlayers));
            _persistence.CompleteFlush(dirtyPlayers);
            timer.Stop();

            PersistentLogger.Info(
                nameof(Database),
                $"Flushed {dirtyPlayers.Count} player snapshot(s) for '{reason}' in {timer.Elapsed.TotalMilliseconds:F1}ms. dirtyRemaining={DirtyCount}, queueDepth={PendingQueueDepth}",
                mirrorConsole: false);
        }

        private static void SaveSnapshotBatch(SqliteConnection connection, IReadOnlyList<DirtyPlayerProgress> dirtyPlayers)
        {
            var snapshots = dirtyPlayers.Select(entry => entry.Snapshot).ToArray();

            using var transaction = connection.BeginTransaction();

            connection.Execute(PlayerUpsertSql,
                snapshots.Select(snapshot => new
                {
                    steamid = snapshot.SteamId,
                    currentRace = snapshot.CurrentRace,
                    name = snapshot.PlayerName
                }),
                transaction);

            connection.Execute(RaceUpsertSql,
                snapshots.Select(snapshot => new
                {
                    steamid = snapshot.SteamId,
                    racename = snapshot.CurrentRace,
                    currentXp = snapshot.CurrentXp,
                    currentLevel = snapshot.CurrentLevel,
                    amountToLevel = snapshot.AmountToLevel,
                    ability1Level = snapshot.Ability1Level,
                    ability2Level = snapshot.Ability2Level,
                    ability3Level = snapshot.Ability3Level,
                    ability4Level = snapshot.Ability4Level
                }),
                transaction);

            transaction.Commit();
        }

        private static void InitializeSchema(SqliteConnection connection)
        {
            connection.Execute("PRAGMA journal_mode=WAL;");
            connection.Execute("PRAGMA synchronous=NORMAL;");
            connection.Execute("PRAGMA busy_timeout=5000;");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS `players` (
                    `steamid` UNSIGNED BIG INT NOT NULL,
                    `currentRace` VARCHAR(32) NOT NULL,
                    `name` VARCHAR(64),
                    PRIMARY KEY (`steamid`));");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS `raceinformation` (
                    `steamid` UNSIGNED BIG INT NOT NULL,
                    `racename` VARCHAR(32) NOT NULL,
                    `currentXP` INT NULL DEFAULT 0,
                    `currentLevel` INT NULL DEFAULT 0,
                    `amountToLevel` INT NULL DEFAULT 100,
                    `ability1level` TINYINT NULL DEFAULT 0,
                    `ability2level` TINYINT NULL DEFAULT 0,
                    `ability3level` TINYINT NULL DEFAULT 0,
                    `ability4level` TINYINT NULL DEFAULT 0,
                    PRIMARY KEY (`steamid`, `racename`));");
        }

        private static int GetInitialAmountToLevel()
        {
            return WarcraftPlugin.Instance.XpSystem?.GetXpForLevel(0) ?? 100;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _dispatcher?.ShutdownAsync("database-dispose", TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _disposed = true;
        }
    }

    internal class DatabasePlayer
    {
        internal long SteamId { get; set; }
        internal string CurrentRace { get; set; }
        internal string Name { get; set; }
    }

    internal class ClassInformation
    {
        internal long SteamId { get; set; }
        internal string RaceName { get; set; }
        internal int CurrentXp { get; set; }
        internal int CurrentLevel { get; set; }
        internal int AmountToLevel { get; set; }
        internal int Ability1Level { get; set; }
        internal int Ability2Level { get; set; }
        internal int Ability3Level { get; set; }
        internal int Ability4Level { get; set; }
    }
}
