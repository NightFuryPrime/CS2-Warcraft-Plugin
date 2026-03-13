using System;
using System.Drawing;
using System.Text;
using WarcraftPlugin.Core;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Menu.WarcraftMenu
{
    internal static class SkillsMenu
    {
        private const int AbilitiesPerPage = 2;

        internal static void Show(WarcraftPlayer wcPlayer, int selectedOptionIndex = 0)
        {
            var plugin = WarcraftPlugin.Instance;
            var localizer = wcPlayer.GetLocalizer();
            var warcraftClass = wcPlayer.GetClass();
            var freePoints = AbilityProgression.GetFreeSkillPoints(wcPlayer);
            var classColor = warcraftClass.DefaultColor.AdjustBrightness(1.3f).ToHex();

            var pointsColor = freePoints > 0 ? "#90EE90" : "#888888";
            var pointsText = freePoints > 0
                ? $"<font color='{pointsColor}' class='{FontSizes.FontSizeS}'>▲ {localizer["menu.skills.available", freePoints]}</font>"
                : $"<font color='{pointsColor}' class='{FontSizes.FontSizeS}'>{localizer["menu.skills.available", freePoints]}</font>";

            var builder = MenuBuilder.Create(
                $"<font color='{classColor}' class='{FontSizes.FontSizeM}'>{warcraftClass.LocalizedDisplayName}</font>" +
                $"<font color='gold' class='{FontSizes.FontSizeSm}'> - {localizer["menu.skills.level"]} {wcPlayer.GetLevel()}</font><br>" +
                pointsText, 3);

            int totalAbilities = warcraftClass.Abilities.Count;
            bool needsPaging = totalAbilities > AbilitiesPerPage;

            void AddAbility(int i)
            {
                var ability = warcraftClass.GetAbility(i);
                var abilityLevel = wcPlayer.GetAbilityLevel(i);
                var maxAbilityLevel = WarcraftPlayer.GetMaxAbilityLevel(i);
                var isUltimate = i == WarcraftPlayer.UltimateAbilityIndex;
                var isMaxed = abilityLevel == maxAbilityLevel;
                var isDisabled = isMaxed || freePoints == 0;

                // Couleur principale selon l'état
                Color nameColor;
                if (isUltimate && !wcPlayer.IsMaxLevel && !isMaxed)
                    nameColor = Color.Gray;
                else if (isMaxed)
                    nameColor = isUltimate ? Color.MediumPurple : Color.Gold;
                else if (isDisabled)
                    nameColor = Color.Gray;
                else
                    nameColor = isUltimate ? Color.MediumPurple : Color.White;

                // Barre de progression visuelle
                var progressBar = BuildProgressBar(abilityLevel, maxAbilityLevel, nameColor);

                string displayString;
                if (isUltimate && !wcPlayer.IsMaxLevel && !isMaxed)
                {
                    displayString =
                        $"<font color='{Color.Gray.ToHex()}' class='{FontSizes.FontSizeSm}'>{ability.DisplayName}</font>" +
                        $"<font color='#555555' class='{FontSizes.FontSizeS}'> ({localizer["menu.skills.ultimate.level", WarcraftPlugin.MaxLevel]})</font>";
                }
                else
                {
                    displayString =
                        $"<font color='{nameColor.ToHex()}' class='{FontSizes.FontSizeSm}'>{ability.DisplayName}</font>" +
                        $"  {progressBar}";
                }

                var subDisplayString = $"<font color='#AAAAAA' class='{FontSizes.FontSizeS}'>{ability.Description}</font>";

                var abilityIndex = i;
                var isDisabledCapture = isDisabled;
                builder.AddOption(displayString, subDisplayString, (p, opt) =>
                {
                    if (!isDisabledCapture)
                        wcPlayer.GrantAbilityLevel(abilityIndex);
                    else
                        p.PlayLocalSound("sounds/ui/menu_invalid.vsnd");

                    Show(wcPlayer, opt.Index);
                });
            }

            // Page 1 : compétences 0 à AbilitiesPerPage-1
            for (int i = 0; i < Math.Min(AbilitiesPerPage, totalAbilities); i++)
                AddAbility(i);

            // Bouton de navigation page suivante
            if (needsPaging)
            {
                int nextPageStart = AbilitiesPerPage + 1;
                builder.AddOption(
                    $"<font color='#00BFFF' class='{FontSizes.FontSizeSm}'>━━ {localizer["menu.skills.next.page"]}</font>",
                    string.Empty,
                    (p, opt) => Show(wcPlayer, nextPageStart));
            }

            // Bouton de navigation page précédente
            if (needsPaging)
            {
                builder.AddOption(
                    $"<font color='#00BFFF' class='{FontSizes.FontSizeSm}'>{localizer["menu.skills.prev.page"]} ━━</font>",
                    string.Empty,
                    (p, opt) => Show(wcPlayer, 0));
            }

            // Page 2 : compétences AbilitiesPerPage à totalAbilities-1
            for (int i = AbilitiesPerPage; i < totalAbilities; i++)
                AddAbility(i);

            MenuManager.OpenMainMenu(wcPlayer.Player, builder.Build(), selectedOptionIndex);
        }

        private static string BuildProgressBar(int current, int max, Color filledColor)
        {
            var sb = new StringBuilder();
            for (int j = 0; j < max; j++)
            {
                if (j < current)
                    sb.Append($"<font color='{filledColor.ToHex()}'>■</font>");
                else
                    sb.Append("<font color='#444444'>□</font>");
            }
            return sb.ToString();
        }
    }
}
