using WarcraftPlugin.Core;
using Xunit;

namespace WarcraftPlugin.Tests;

public class PersistenceCoordinatorTests
{
    [Fact]
    public void CompleteFlushOnlyClearsMatchingDirtyVersion()
    {
        var coordinator = new PersistenceCoordinator();
        var first = CreateSnapshot(76561198000000001, "ranger", xp: 10);
        var second = CreateSnapshot(76561198000000001, "ranger", xp: 20);

        var firstDirty = coordinator.MarkDirty(first);
        var secondDirty = coordinator.MarkDirty(second);

        coordinator.CompleteFlush([firstDirty]);

        Assert.True(coordinator.TryGetDirty(second.SteamId, out var currentDirty));
        Assert.Equal(secondDirty.Version, currentDirty.Version);
        Assert.Equal(20, currentDirty.Snapshot.CurrentXp);

        coordinator.CompleteFlush([secondDirty]);

        Assert.False(coordinator.TryGetDirty(second.SteamId, out _));
    }

    private static PlayerProgressSnapshot CreateSnapshot(long steamId, string race, int xp)
    {
        return new PlayerProgressSnapshot(
            steamId,
            "Player",
            race,
            xp,
            CurrentLevel: 1,
            AmountToLevel: 110,
            Ability1Level: 1,
            Ability2Level: 0,
            Ability3Level: 0,
            Ability4Level: 0);
    }
}
