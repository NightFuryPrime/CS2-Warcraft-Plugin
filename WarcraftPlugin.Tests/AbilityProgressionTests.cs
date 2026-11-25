using WarcraftPlugin.Core;
using Xunit;

namespace WarcraftPlugin.Tests;

public class AbilityProgressionTests
{
    [Fact]
    public void GetFreeSkillPointsSubtractsSpentPoints()
    {
        var freePoints = AbilityProgression.GetFreeSkillPoints(6, [1, 1, 1, 1]);

        Assert.Equal(2, freePoints);
    }

    [Fact]
    public void GetFreeSkillPointsClampsLevelAndNeverReturnsNegative()
    {
        var freePoints = AbilityProgression.GetFreeSkillPoints(99, [5, 5, 5, 5]);

        Assert.Equal(0, freePoints);
    }
}
