using CS2MenuManager.API.Class;
using Xunit;

namespace WarcraftPlugin.Tests;

public class MenuSessionTrackerTests
{
    [Fact]
    public void NewOpenInvalidatesAnEarlierPendingOpenForTheSamePlayer()
    {
        var tracker = new MenuSessionTracker();
        const ulong steamId = 1234;

        var firstOpen = tracker.Begin(steamId);
        var secondOpen = tracker.Begin(steamId);

        Assert.False(tracker.IsCurrent(steamId, firstOpen));
        Assert.True(tracker.IsCurrent(steamId, secondOpen));
    }

    [Fact]
    public void CancellationAndGlobalCleanupCannotReuseAnOldGeneration()
    {
        var tracker = new MenuSessionTracker();
        const ulong steamId = 1234;

        var firstOpen = tracker.Begin(steamId);
        tracker.Cancel(steamId);
        var secondOpen = tracker.Begin(steamId);
        tracker.CancelAll();
        var thirdOpen = tracker.Begin(steamId);

        Assert.False(tracker.IsCurrent(steamId, firstOpen));
        Assert.False(tracker.IsCurrent(steamId, secondOpen));
        Assert.True(tracker.IsCurrent(steamId, thirdOpen));
    }
}
