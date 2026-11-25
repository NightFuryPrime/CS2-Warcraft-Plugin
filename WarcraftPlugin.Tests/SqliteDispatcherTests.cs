using Dapper;
using WarcraftPlugin.Core;
using WarcraftPlugin.Menu;
using Xunit;

namespace WarcraftPlugin.Tests;

public class SqliteDispatcherTests
{
    [Fact]
    public async Task ExecuteAsyncRunsWorkInSubmissionOrder()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"warcraft-plugin-test-{Guid.NewGuid():N}.db");
        var dispatcher = new SqliteDispatcher($"Data Source={databasePath};Pooling=False");

        try
        {
            var sqliteVersion = await dispatcher.ExecuteAsync("sqlite-version",
                connection => connection.ExecuteScalar<string>("SELECT sqlite_version();"));
            Assert.Equal("3.54.1", sqliteVersion);

            await dispatcher.ExecuteAsync("schema", connection =>
                connection.Execute("CREATE TABLE values_seen (id INTEGER PRIMARY KEY AUTOINCREMENT, value INTEGER NOT NULL);"));

            await dispatcher.ExecuteAsync("insert-1", connection =>
                connection.Execute("INSERT INTO values_seen (value) VALUES (1);"));
            await dispatcher.ExecuteAsync("insert-2", connection =>
                connection.Execute("INSERT INTO values_seen (value) VALUES (2);"));

            var values = await dispatcher.ExecuteAsync("read", connection =>
                connection.Query<int>("SELECT value FROM values_seen ORDER BY id;").AsList());

            Assert.Equal([1, 2], values);
            await dispatcher.DrainAsync("test", TimeSpan.FromSeconds(5));
            Assert.Equal(0, dispatcher.PendingCount);
        }
        finally
        {
            dispatcher.Dispose();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task DatabasePersistsMenuPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"warcraft-plugin-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var database = new Database();

        try
        {
            database.Initialize(directory);

            var preference = new MenuPreference(WarcraftMenuScale.ExtraLarge, WarcraftMenuType.CenterHtmlMenu);
            await database.SaveMenuPreferenceAsync(76561198000000001, preference);

            var stored = await database.LoadMenuPreferenceAsync(76561198000000001);

            Assert.NotNull(stored);
            Assert.Equal("ExtraLarge", stored.MenuScale);
            Assert.Equal("CenterHtmlMenu", stored.MenuType);
        }
        finally
        {
            database.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
