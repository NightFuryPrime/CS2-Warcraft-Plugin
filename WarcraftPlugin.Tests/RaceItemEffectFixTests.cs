using WarcraftPlugin.Classes;
using WarcraftPlugin.Core;
using WarcraftPlugin.Events;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Menu;
using WarcraftPlugin.Models;
using WarcraftPlugin.Summons;
using Xunit;

namespace WarcraftPlugin.Tests;

public class RaceItemEffectFixTests
{
    [Fact]
    public void RuntimeAssemblyUsesVersion420()
    {
        Assert.Equal("4.2.0.0", typeof(global::WarcraftPlugin.WarcraftPlugin).Assembly.GetName().Version?.ToString());
    }

    [Fact]
    public void DroneLineOfSightRequiresAClearTraceOrTheIntendedTarget()
    {
        const int target = 12;

        Assert.True(new RayTracer.LineTrace(RayTracer.TraceState.Clear, null, 0).Reaches(target));
        Assert.True(new RayTracer.LineTrace(RayTracer.TraceState.Hit, null, target).Reaches(target));
        Assert.False(new RayTracer.LineTrace(RayTracer.TraceState.Hit, null, 99).Reaches(target));
        Assert.False(new RayTracer.LineTrace(RayTracer.TraceState.Hit, null, 0).Reaches(target));
        Assert.False(default(RayTracer.LineTrace).Reaches(target));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(3, 30)]
    [InlineData(5, 50)]
    [InlineData(99, 50)]
    public void DroneRocketChanceClampsBySkillLevel(int level, int expected)
    {
        Assert.Equal(expected, Drone.GetRocketChance(level));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(3, 20)]
    [InlineData(5, 30)]
    [InlineData(99, 30)]
    public void HammerstormWarcryHealthBonusClampsBySkillLevel(int level, int expected)
    {
        Assert.Equal(expected, Hammerstorm.GetWarcryHealthBonus(level));
    }

    [Fact]
    public void MenuPreferenceFallsBackToConfigThenSafeDefault()
    {
        var config = new Config
        {
            DefaultMenuScale = "ExtraLarge",
            DefaultWarcraftMenuType = "CenterHtmlMenu"
        };

        var preference = MenuPreferenceResolver.Resolve(config);

        Assert.Equal(WarcraftMenuScale.ExtraLarge, preference.Scale);
        Assert.Equal(WarcraftMenuType.CenterHtmlMenu, preference.Type);

        var invalidConfig = new Config
        {
            DefaultMenuScale = "Huge",
            DefaultWarcraftMenuType = "Unknown"
        };

        Assert.Equal(MenuPreferenceResolver.SafeDefault, MenuPreferenceResolver.Resolve(invalidConfig));
    }

    [Fact]
    public void StoredMenuPreferenceFallsBackPerInvalidField()
    {
        var config = new Config
        {
            DefaultMenuScale = "Normal",
            DefaultWarcraftMenuType = "ChatMenu"
        };

        var preference = MenuPreferenceResolver.Resolve(config, new StoredMenuPreference
        {
            MenuScale = "Large",
            MenuType = "InvalidType"
        });

        Assert.Equal(WarcraftMenuScale.Large, preference.Scale);
        Assert.Equal(WarcraftMenuType.ChatMenu, preference.Type);
    }

    [Theory]
    [InlineData("<font class='fontSize-sm'>Text</font>", (int)WarcraftMenuScale.Small, "fontSize-s")]
    [InlineData("<font class='fontSize-sm'>Text</font>", (int)WarcraftMenuScale.Normal, "fontSize-sm")]
    [InlineData("<font class='fontSize-sm'>Text</font>", (int)WarcraftMenuScale.Large, "fontSize-m")]
    [InlineData("<font class='fontSize-sm'>Text</font>", (int)WarcraftMenuScale.ExtraLarge, "fontSize-l")]
    public void MenuScaleMapsFontClasses(string html, int scale, string expectedClass)
    {
        Assert.Contains(expectedClass, MenuText.ApplyScale(html, (WarcraftMenuScale)scale));
    }

    [Fact]
    public void DamageReductionCalculatesPreventedDamage()
    {
        var result = DamageMath.CalculateReduction(incomingHealth: 100, incomingArmor: 40, reduction: 0.25);

        Assert.Equal(75, result.ReducedHealth);
        Assert.Equal(30, result.ReducedArmor);
        Assert.Equal(25, result.PreventedHealth);
        Assert.Equal(10, result.PreventedArmor);
    }

    [Theory]
    [InlineData(359f, 1f, 2f)]
    [InlineData(10f, 350f, 20f)]
    [InlineData(90f, 270f, 180f)]
    public void AngleDeltaHandlesWraparound(float a, float b, float expected)
    {
        Assert.Equal(expected, Geometry.AngleDeltaDegrees(a, b));
    }

    [Theory]
    [InlineData("xm1014")]
    [InlineData("weapon_xm1014")]
    [InlineData(" MAG7 ")]
    public void WeaponTypeNormalizationRecognizesShotguns(string weaponName)
    {
        Assert.True(WeaponTypes.IsShotgun(weaponName));
    }

    [Fact]
    public void HurtOtherDeduperOnlySuppressesDuplicateShotgunHitsInSameTick()
    {
        var deduper = new HurtOtherDeduper();

        Assert.True(deduper.ShouldProcess((nint)1, (nint)2, "xm1014", 10f));
        Assert.False(deduper.ShouldProcess((nint)1, (nint)2, "weapon_xm1014", 10f));
        Assert.True(deduper.ShouldProcess((nint)1, (nint)3, "xm1014", 10f));
        Assert.True(deduper.ShouldProcess((nint)1, (nint)2, "ak47", 10f));
        Assert.True(deduper.ShouldProcess((nint)1, (nint)2, "xm1014", 10.1f));
    }

    [Fact]
    public void QueuedClassChangeRequiresRoundReadinessBeforeSpawnApply()
    {
        var player = new WarcraftPlayer(null!);

        player.QueueClassChange("necromancer");

        Assert.False(player.ShouldApplyQueuedClassChange("mage"));

        player.MarkQueuedClassChangeReady();

        Assert.True(player.ShouldApplyQueuedClassChange("mage"));

        player.ClearQueuedClassChange();

        Assert.False(player.ShouldApplyQueuedClassChange("mage"));
    }

    [Theory]
    [InlineData("weapon_ak47", true)]
    [InlineData("weapon_c4", false)]
    [InlineData("weapon_knife", false)]
    [InlineData("weapon_knife_karambit", false)]
    [InlineData("weapon_flashbang", false)]
    public void ShapeshifterWeaponRestoreFiltersTemporaryWeapons(string designerName, bool expected)
    {
        Assert.Equal(expected, Shapeshifter.ShouldPreserveWeapon(designerName));
    }

    [Theory]
    [InlineData(true, 1, 2, true, 2, 2, false, false, true)]
    [InlineData(true, 1, 2, true, 2, 2, true, false, false)]
    [InlineData(true, 1, 2, true, 2, 3, false, false, false)]
    [InlineData(true, 1, 2, true, 1, 2, false, false, false)]
    [InlineData(true, 1, 2, true, 2, 2, false, true, false)]
    [InlineData(false, 1, 2, true, 2, 2, false, false, false)]
    [InlineData(true, 1, 2, false, 2, 2, false, false, false)]
    public void ReviveTargetingRejectsInvalidBotControlTargets(
        bool casterValid,
        int casterHandle,
        int casterTeam,
        bool targetValid,
        int targetHandle,
        int targetTeam,
        bool targetAlive,
        bool targetControlsCaster,
        bool expected)
    {
        Assert.Equal(expected, BotControl.IsValidReviveTargetState(
            casterValid,
            (nint)casterHandle,
            casterTeam,
            targetValid,
            (nint)targetHandle,
            targetTeam,
            targetAlive,
            targetControlsCaster));
    }

    [Fact]
    public void WarcraftPlayerCapturesNullIdentityForTestPlayers()
    {
        var player = new WarcraftPlayer(null!);

        Assert.Equal(-1, player.CapturedSlot);
        Assert.Equal(nint.Zero, player.CapturedHandle);
        Assert.Equal(0UL, player.CapturedSteamId);
    }
}
