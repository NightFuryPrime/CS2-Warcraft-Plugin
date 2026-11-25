using System;
using System.Collections.Generic;
using System.Linq;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Core
{
    internal static class AbilityProgression
    {
        internal static int GetFreeSkillPoints(WarcraftPlayer wcPlayer)
        {
            if (wcPlayer == null) return 0;

            var abilityCount = wcPlayer.GetClass().Abilities.Count;
            var abilityLevels = Enumerable.Range(0, abilityCount)
                .Select(wcPlayer.GetAbilityLevel);

            return GetFreeSkillPoints(wcPlayer.GetLevel(), abilityLevels);
        }

        internal static int GetFreeSkillPoints(int level, IEnumerable<int> abilityLevels)
        {
            var clampedLevel = Math.Clamp(level, 0, WarcraftPlugin.MaxLevel);
            var totalPointsUsed = abilityLevels?.Sum() ?? 0;
            return Math.Max(0, clampedLevel - totalPointsUsed);
        }

        internal static void AutoSpendSkillPoints(WarcraftPlayer wcPlayer)
        {
            if (wcPlayer == null) return;
            var wcClass = wcPlayer.GetClass();

            while (GetFreeSkillPoints(wcPlayer) > 0)
            {
                var available = Enumerable.Range(0, wcClass.Abilities.Count)
                    .Where(i => wcPlayer.GetAbilityLevel(i) < WarcraftPlayer.GetMaxAbilityLevel(i)
                                && (i != WarcraftPlayer.UltimateAbilityIndex || wcPlayer.IsMaxLevel))
                    .ToList();
                if (available.Count == 0)
                    break;
                var index = available[RandomProvider.Next(available.Count)];
                wcPlayer.GrantAbilityLevel(index);
            }
        }
    }
}
