using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Core
{
    internal static class AbilityBenefitAnnouncer
    {
        private static readonly Regex TieredValuePattern = new(@"(?<values>-?\d+(?:\.\d+)?(?:/-?\d+(?:\.\d+)?)+)(?<suffix>%?)", RegexOptions.Compiled);

        internal static void SendRoundSummary(CCSPlayerController player, WarcraftPlayer warcraftPlayer)
        {
            var warcraftClass = warcraftPlayer?.GetClass();
            if (player == null || warcraftClass == null) return;

            var lines = BuildAbilityLines(warcraftPlayer, warcraftClass);
            if (lines.Count == 0) return;

            player.PrintToChat(
                $" {ChatColors.Gold}[{warcraftClass.LocalizedDisplayName}] {ChatColors.Green}{WarcraftPlugin.Instance.Localizer["ability.benefits.header", lines.Count]}");

            foreach (var line in lines)
            {
                player.PrintToChat(line);
            }
        }

        private static List<string> BuildAbilityLines(WarcraftPlayer player, WarcraftClass warcraftClass)
        {
            var lines = new List<string>();

            for (int i = 0; i < warcraftClass.Abilities.Count; i++)
            {
                var level = player.GetAbilityLevel(i);
                if (level <= 0) continue;

                var ability = warcraftClass.GetAbility(i);
                var formattedDescription = FormatDescription(ability.Description, level);

                lines.Add(
                    $" {ChatColors.Blue}{ability.DisplayName}{ChatColors.Gold} Lv{level}{ChatColors.Default}: {formattedDescription}");
            }

            return lines;
        }

        private static string FormatDescription(string description, int level)
        {
            if (string.IsNullOrWhiteSpace(description)) return description;

            return TieredValuePattern.Replace(description, match =>
            {
                var values = match.Groups["values"].Value.Split('/');
                var suffix = match.Groups["suffix"].Value;
                var index = Math.Clamp(level - 1, 0, values.Length - 1);
                return values[index] + suffix;
            });
        }
    }
}
