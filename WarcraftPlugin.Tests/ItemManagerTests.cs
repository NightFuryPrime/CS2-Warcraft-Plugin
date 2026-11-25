using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Core;
using WarcraftPlugin.Items;
using WarcraftPlugin.Models;
using Xunit;

namespace WarcraftPlugin.Tests;

public class ItemManagerTests
{
    [Fact]
    public void AddItemRejectsDuplicateItemTypes()
    {
        var player = new WarcraftPlayer(null!);

        Assert.True(player.AddItem(new TestItem()));
        Assert.False(player.AddItem(new TestItem()));
    }

    private sealed class TestItem : ShopItem
    {
        protected override string Name => "Test Item";
        protected override FormattableString Description => $"Test item";
        internal override int Price { get; set; } = 1;

        internal override void Apply(CCSPlayerController player) { }
    }
}
