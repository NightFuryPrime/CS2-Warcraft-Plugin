using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Dapper;
using Microsoft.Data.Sqlite;
using WarcraftPlugin.Models;
using System.Threading;
using System.Threading.Tasks;

namespace WarcraftPlugin.Core
{
    internal class Database : IDisposable
    {
        private SqliteConnection _connection;
        private bool _disposed;
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        internal void Initialize(string directory)
        {
            _connection =
                new SqliteConnection(
                    $"Data Source={Path.Join(directory, "database.db")}");

            _connection.Execute(@"
                CREATE TABLE IF NOT EXISTS `players` (
	                `steamid` UNSIGNED BIG INT NOT NULL,
	                `currentRace` VARCHAR(32) NOT NULL,
                  `name` VARCHAR(64),
	                PRIMARY KEY (`steamid`));");

            _connection.Execute(@"
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
                  PRIMARY KEY (`steamid`, `racename`));
                ");
        }

        internal async Task<bool> PlayerExistsInDatabase(ulong steamid)
        {
            await _dbLock.WaitAsync();
            try
            {
                return await _connection.ExecuteScalarAsync<int>("select count(*) from players where steamid = @steamid",
                    new { steamid = (long)steamid }) > 0;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        internal async Task AddNewPlayerToDatabase(CCSPlayerController player)
        {
            var defaultClass = WarcraftPlugin.Instance.classManager.GetDefaultClass();
            Console.WriteLine($"Adding client to database {player.SteamID}");
            await _dbLock.WaitAsync();
            try
            {
                await _connection.ExecuteAsync(@"
            INSERT INTO players (`steamid`, `currentRace`)
            VALUES(@steamid, @className)",
                    new { steamid = (long)player.SteamID, className = defaultClass.InternalName });
            }
            finally
            {
                _dbLock.Release();
            }
        }

        internal async Task<WarcraftPlayer> LoadPlayerFromDatabase(CCSPlayerController player, XpSystem xpSystem)
        {
            DatabasePlayer dbPlayer;
            long steamId = (long)player.SteamID;
            await _dbLock.WaitAsync();
            try
            {
                dbPlayer = await _connection.QueryFirstOrDefaultAsync<DatabasePlayer>(@"
            SELECT * FROM `players` WHERE `steamid` = @steamid",
                    new { steamid = steamId });
            }
            finally
            {
                _dbLock.Release();
            }

            if (dbPlayer == null)
            {
                Console.WriteLine($"[Warcraft] Player {player.PlayerName} ({steamId}) not found in DB, creating...");
                await AddNewPlayerToDatabase(player);
                await _dbLock.WaitAsync();
                try
                {
                    dbPlayer = await _connection.QueryFirstOrDefaultAsync<DatabasePlayer>(@"
                    SELECT * FROM `players` WHERE `steamid` = @steamid",
                        new { steamid = steamId });
                }
                finally
                {
                    _dbLock.Release();
                }
            }

            if (dbPlayer == null)
            {
                Console.WriteLine($"[Warcraft] CRITICAL: Failed to load player {player.PlayerName} ({steamId}) even after creation.");
                return null;
            }

            // If the class no longer exists, set it to the default class
            if (!WarcraftPlugin.Instance.classManager.GetAllClasses().Any(x => x.InternalName == dbPlayer.CurrentRace))
            {
                var defaultClass = WarcraftPlugin.Instance.classManager.GetDefaultClass();
                dbPlayer.CurrentRace = defaultClass.InternalName;
                player.PrintToChat(" " + WarcraftPlugin.Instance.Localizer["class.disabled", defaultClass.LocalizedDisplayName]);

                await _dbLock.WaitAsync();
                try
                {
                    await _connection.ExecuteAsync("UPDATE players SET currentRace = @race WHERE steamid = @steamid",
                        new { race = dbPlayer.CurrentRace, steamid = (long)player.SteamID });
                }
                finally
                {
                    _dbLock.Release();
                }
            }

            bool raceInformationExists;
            await _dbLock.WaitAsync();
            try
            {
                raceInformationExists = await _connection.ExecuteScalarAsync<int>(@"
            select count(*) from `raceinformation` where steamid = @steamid AND racename = @racename",
                    new { steamid = steamId, racename = dbPlayer.CurrentRace }
                ) > 0;

                if (!raceInformationExists)
                {
                    await _connection.ExecuteAsync(@"
                insert into `raceinformation` (steamid, racename, currentLevel)
                values (@steamid, @racename, 0);",
                        new { steamid = (long)player.SteamID, racename = dbPlayer.CurrentRace });
                }

                var raceInformation = await _connection.QueryFirstAsync<ClassInformation>(@"
            SELECT * from `raceinformation` where `steamid` = @steamid AND `racename` = @racename",
                    new { steamid = steamId, racename = dbPlayer.CurrentRace });

                var wcPlayer = new WarcraftPlayer(player);
                wcPlayer.LoadClassInformation(raceInformation, xpSystem);

                return wcPlayer;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        internal async Task<List<ClassInformation>> LoadClassInformationFromDatabase(CCSPlayerController player)
        {
            await _dbLock.WaitAsync();
            try
            {
                var raceInformation = await _connection.QueryAsync<ClassInformation>(@"
            SELECT * from `raceinformation` where `steamid` = @steamid",
                    new { steamid = (long)player.SteamID });

                return raceInformation.AsList();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        internal async Task SavePlayerToDatabase(CCSPlayerController player)
        {
            var wcPlayer = WarcraftPlugin.Instance.GetWcPlayer(player);
            if (wcPlayer == null) return;

            Server.PrintToConsole($"Saving {player.PlayerName} to database...");

            await _dbLock.WaitAsync();
            try
            {
                var raceInformationExists = await _connection.ExecuteScalarAsync<int>(@"
            select count(*) from `raceinformation` where steamid = @steamid AND racename = @racename",
                    new { steamid = (long)player.SteamID, racename = wcPlayer.className }
                ) > 0;

                if (!raceInformationExists)
                {
                    await _connection.ExecuteAsync(@"
                insert into `raceinformation` (steamid, racename)
                values (@steamid, @racename);",
                        new { steamid = (long)player.SteamID, racename = wcPlayer.className });
                }

                await _connection.ExecuteAsync(@"
                UPDATE `raceinformation` SET `currentXP` = @currentXp,
                 `currentLevel` = @currentLevel,
                 `ability1level` = @ability1Level,
                 `ability2level` = @ability2Level,
                 `ability3level` = @ability3Level,
                 `ability4level` = @ability4Level,
                 `amountToLevel` = @amountToLevel WHERE `steamid` = @steamid AND `racename` = @racename;",
                    new
                    {
                        wcPlayer.currentXp,
                        wcPlayer.currentLevel,
                        ability1Level = wcPlayer.GetAbilityLevel(0),
                        ability2Level = wcPlayer.GetAbilityLevel(1),
                        ability3Level = wcPlayer.GetAbilityLevel(2),
                        ability4Level = wcPlayer.GetAbilityLevel(3),
                        wcPlayer.amountToLevel,
                        steamid = (long)player.SteamID,
                        racename = wcPlayer.className
                    });
            }
            finally
            {
                _dbLock.Release();
            }
        }

        internal async Task SaveClients()
        {
            var saveTasks = new List<Task>();
            foreach (var player in Utilities.GetPlayers())
            {
                if (!player.IsValid) continue;

                var wcPlayer = WarcraftPlugin.Instance.GetWcPlayer(player);
                if (wcPlayer == null) continue;

                saveTasks.Add(SavePlayerToDatabase(player));
            }
            await Task.WhenAll(saveTasks);
        }

        internal async Task SaveCurrentClass(CCSPlayerController player, string className)
        {
            if (player == null || !player.IsValid || string.IsNullOrWhiteSpace(className))
                return;

            await _dbLock.WaitAsync();
            try
            {
                await _connection.ExecuteAsync(@"
            UPDATE `players` SET `currentRace` = @currentRace, `name` = @name WHERE `steamid` = @steamid;",
                    new
                    {
                        currentRace = className,
                        name = player.PlayerName,
                        steamid = (long)player.SteamID
                    });
            }
            finally
            {
                _dbLock.Release();
            }
        }

        internal void ResetClients()
        {
            // This is a destructive operation, maybe keep it sync or make async?
            // Keeping sync for now but locking.
            _dbLock.Wait();
            try
            {
                _connection.Execute(@"
                DELETE FROM `players`;");

                _connection.Execute(@"
                DELETE FROM `raceinformation`;");
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _connection?.Dispose();
            _dbLock?.Dispose();
            _disposed = true;
        }
    }

    internal class DatabasePlayer
    {
        // Dapper returns integer values from SQLite as long (Int64) which
        // cannot be automatically cast to ulong. Using a signed integer here
        // avoids InvalidCastException when mapping query results.
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
