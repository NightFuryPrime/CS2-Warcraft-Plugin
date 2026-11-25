using WarcraftPlugin.Menu.WarcraftMenu;
using Xunit;

namespace WarcraftPlugin.Tests;

public class ClassMenuTests
{
    [Theory]
    [InlineData(50, 49, true)]
    [InlineData(50, 50, false)]
    [InlineData(0, 0, false)]
    public void IsClassLockedUsesInclusiveUnlockThreshold(int requiredTotal, int totalLevels, bool expected)
    {
        Assert.Equal(expected, ClassMenu.IsClassLocked(requiredTotal, totalLevels));
    }
}
