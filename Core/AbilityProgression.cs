using System;
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

            int totalPointsUsed = 0;
            var abilityCount = wcPlayer.GetClass().Abilities.Count;
            for (int i = 0; i < abilityCount; i++)
            {
                totalPointsUsed += wcPlayer.GetAbilityLevel(i);
            }

            int level = wcPlayer.GetLevel();
            if (level > WarcraftPlugin.MaxLevel)
                level = WarcraftPlugin.MaxSkillLevel;

            return level - totalPointsUsed;
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
