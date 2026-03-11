using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Core
{
    internal class XpSystem
    {
        private readonly WarcraftPlugin _plugin;
        private readonly Config _config;
        private readonly IStringLocalizer _localizer;

        internal XpSystem(WarcraftPlugin plugin, Config config, IStringLocalizer localizer)
        {
            _plugin = plugin;
            _config = config;
            _localizer = localizer;
        }

        private readonly List<int> _levelXpRequirement = [.. new int[256]];

        internal void GenerateXpCurve(int initial, float modifier, int maxLevel)
        {
            for (int i = 0; i <= maxLevel; i++)
            {
                if (i == 0)
                    _levelXpRequirement[i] = initial;
                else
                    _levelXpRequirement[i] = Convert.ToInt32(_levelXpRequirement[i - 1] * modifier);
            }
        }

        internal int GetXpForLevel(int level)
        {
            return _levelXpRequirement[level];
        }

        internal void AddXp(CCSPlayerController player, int xpToAdd)
        {
            var wcPlayer = _plugin.GetWcPlayer(player);
            if (wcPlayer == null) return;

            if (wcPlayer.GetLevel() >= WarcraftPlugin.MaxLevel) return;

            wcPlayer.currentXp += xpToAdd;

            while (wcPlayer.currentXp >= wcPlayer.amountToLevel)
            {
                wcPlayer.currentXp = wcPlayer.currentXp - wcPlayer.amountToLevel;
                GrantLevel(wcPlayer);

                if (wcPlayer.GetLevel() >= WarcraftPlugin.MaxLevel) return;
            }

            _plugin.SavePlayerProgress(player);
        }

        internal void AddXpWithMessage(CCSPlayerController player, int xpToAdd, string localizationKey, params object[] extraArgs)
        {
            if (player == null || xpToAdd <= 0) return;

            AddXp(player, xpToAdd);

            var argsLength = extraArgs?.Length ?? 0;
            var formatterArgs = new object[1 + argsLength];
            formatterArgs[0] = xpToAdd;
            if (argsLength > 0)
            {
                Array.Copy(extraArgs, 0, formatterArgs, 1, argsLength);
            }

            player.PrintToChat($" {_localizer[localizationKey, formatterArgs]}");
        }

        internal void GrantLevel(WarcraftPlayer wcPlayer)
        {
            if (wcPlayer.GetLevel() >= WarcraftPlugin.MaxLevel) return;

            wcPlayer.currentLevel += 1;

            RecalculateXpForLevel(wcPlayer);
            PerformLevelupEvents(wcPlayer);
            if (wcPlayer.GetPlayer().IsBot)
            {
                AbilityProgression.AutoSpendSkillPoints(wcPlayer);
            }
        }

        private static void PerformLevelupEvents(WarcraftPlayer wcPlayer)
        {
            var player = wcPlayer.GetPlayer();
            if (player.IsAlive())
            {
                player.PlayLocalSound("sounds/ui/achievement_earned.vsnd", 0.35f);
                Warcraft.SpawnParticle(player.PlayerPawn.Value.AbsOrigin, "particles/ui/ammohealthcenter/ui_hud_kill_streaks_glow_5.vpcf", 1);
            }

            WarcraftPlugin.RefreshPlayerName(player);
        }

        internal void RecalculateXpForLevel(WarcraftPlayer wcPlayer)
        {
            if (wcPlayer.currentLevel == WarcraftPlugin.MaxLevel)
            {
                wcPlayer.amountToLevel = 0;
                return;
            }

            wcPlayer.amountToLevel = GetXpForLevel(wcPlayer.currentLevel);
        }

        [Obsolete("Use AbilityProgression.GetFreeSkillPoints instead")]
        internal static int GetFreeSkillPoints(WarcraftPlayer wcPlayer) => AbilityProgression.GetFreeSkillPoints(wcPlayer);

        [Obsolete("Use AbilityProgression.AutoSpendSkillPoints instead")]
        internal static void AutoSpendSkillPoints(WarcraftPlayer wcPlayer) => AbilityProgression.AutoSpendSkillPoints(wcPlayer);

        internal void CalculateAndAddKillXp(
            CCSPlayerController attacker,
            CCSPlayerController victim,
            string weaponName,
            bool headshot)
        {
            if (attacker == null || victim == null) return;

            var xpHeadshot = 0f;
            var xpKnife = 0f;

            if (headshot)
                xpHeadshot = Convert.ToInt32(_config.XpPerKill * _config.XpHeadshotModifier);

            if (weaponName.StartsWith("knife"))
            {
                xpKnife = Convert.ToInt32(_config.XpPerKill * _config.XpKnifeModifier);
            }

            var xpToAdd = Convert.ToInt32(_config.XpPerKill + xpHeadshot + xpKnife);
            var levelBonus = 0;
            if (_config.EnableLevelDifferenceXp)
            {
                var attackerWc = _plugin.GetWcPlayer(attacker);
                var victimWc = _plugin.GetWcPlayer(victim);
                if (attackerWc != null && victimWc != null)
                {
                    var diff = victimWc.GetLevel() - attackerWc.GetLevel();
                    if (diff > 0)
                    {
                        var multiplier = 1 + (diff * 2f / (WarcraftPlugin.MaxLevel - 1));
                        var newXp = Convert.ToInt32(xpToAdd * multiplier);
                        levelBonus = newXp - xpToAdd;
                        xpToAdd = newXp;
                    }
                }
            }

            AddXp(attacker, xpToAdd);

            string hsBonus = xpHeadshot != 0 ? $"(+{xpHeadshot} {_localizer["xp.bonus.headshot"]})" : "";
            string knifeBonus = xpKnife != 0 ? $"(+{xpKnife} {_localizer["xp.bonus.knife"]})" : "";
            string levelDiffBonus = levelBonus > 0 ? $"(+{levelBonus} {_localizer["xp.bonus.level"]})" : "";

            string xpString = $" {_localizer["xp.kill", xpToAdd, victim.PlayerName, hsBonus, knifeBonus, levelDiffBonus]}";
            attacker.PrintToChat(xpString);
        }
    }
}
