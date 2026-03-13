using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Generic;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Menu.WarcraftMenu
{
    internal static class SettingsMenu
    {
        private static readonly Dictionary<string, string> AvailableLanguages = new()
        {
            { "en", "English" },
            { "fr", "Français" },
            { "de", "Deutsch" },
            { "ru", "Русский" },
            { "da", "Dansk" },
            { "tr", "Türkçe" },
        };

        internal static void Show(WarcraftPlayer wcPlayer)
        {
            if (wcPlayer == null) return;

            var plugin = WarcraftPlugin.Instance;
            var localizer = wcPlayer.GetLocalizer();
            var player = wcPlayer.Player;

            var builder = MenuBuilder.Create(
                $"<font color='#00BFFF' class='{FontSizes.FontSizeM}'>⚙ {localizer["menu.settings"]}</font>", 4);

            // Toggle préfixe de nom
            var prefixEnabled = !wcPlayer.HideNamePrefix;
            var prefixStatusColor = prefixEnabled ? "#90EE90" : "#FF4444";
            var prefixStatusText = prefixEnabled
                ? localizer["menu.settings.enabled"].ToString()
                : localizer["menu.settings.disabled"].ToString();

            builder.AddOption(
                $"<font color='white' class='{FontSizes.FontSizeSm}'>{localizer["menu.settings.nameprefix"]}</font>" +
                $"  <font color='{prefixStatusColor}' class='{FontSizes.FontSizeS}'>[{prefixStatusText}]</font>",
                $"<font color='#888888' class='{FontSizes.FontSizeS}'>{localizer["menu.settings.nameprefix.desc"]}</font>",
                (p, opt) =>
                {
                    wcPlayer.HideNamePrefix = !wcPlayer.HideNamePrefix;
                    WarcraftPlugin.RefreshPlayerName(player);
                    Show(wcPlayer);
                });

            // Sélection de langue
            var currentLang = string.IsNullOrEmpty(wcPlayer.PreferredLanguage)
                ? plugin.Config.Language
                : wcPlayer.PreferredLanguage;
            AvailableLanguages.TryGetValue(currentLang, out var currentLangName);
            currentLangName ??= currentLang.ToUpper();

            builder.AddOption(
                $"<font color='white' class='{FontSizes.FontSizeSm}'>{localizer["menu.settings.language"]}</font>" +
                $"  <font color='#00BFFF' class='{FontSizes.FontSizeS}'>[{currentLangName}]</font>",
                $"<font color='#888888' class='{FontSizes.FontSizeS}'>{localizer["menu.settings.language.desc"]}</font>",
                (p, opt) => ShowLanguageMenu(wcPlayer));

            // Réinitialiser les skills
            var freePoints = wcPlayer.GetClass().Abilities.Count > 0
                ? $"  <font color='gold' class='{FontSizes.FontSizeS}'>({localizer["menu.skills.available", Core.AbilityProgression.GetFreeSkillPoints(wcPlayer)]})</font>"
                : string.Empty;

            builder.AddOption(
                $"<font color='orange' class='{FontSizes.FontSizeSm}'>↺ {localizer["menu.settings.reset.skills"]}</font>{freePoints}",
                $"<font color='#888888' class='{FontSizes.FontSizeS}'>{localizer["menu.settings.reset.skills.desc"]}</font>",
                (p, opt) =>
                {
                    var abilityCount = wcPlayer.GetClass().Abilities.Count;
                    for (int i = 0; i < abilityCount; i++)
                        wcPlayer.SetAbilityLevel(i, 0);
                    p.PrintToChat($" {ChatColors.Orange}{localizer["menu.settings.reset.skills.done"]}");
                    Show(wcPlayer);
                });

            MenuManager.OpenMainMenu(player, builder.Build());
        }

        private static void ShowLanguageMenu(WarcraftPlayer wcPlayer)
        {
            var localizer = wcPlayer.GetLocalizer();

            var builder = MenuBuilder.Create(
                $"<font color='#00BFFF' class='{FontSizes.FontSizeM}'>⚙ {localizer["menu.settings.language"]}</font>", 6);

            var currentLang = wcPlayer.PreferredLanguage ?? WarcraftPlugin.Instance.Config.Language;

            foreach (var (code, name) in AvailableLanguages)
            {
                var isSelected = string.Equals(currentLang, code, System.StringComparison.OrdinalIgnoreCase);
                var nameColor = isSelected ? "#90EE90" : "white";
                var check = isSelected ? " <font color='#90EE90'>✔</font>" : string.Empty;
                var langCode = code;

                builder.AddOption(
                    $"<font color='{nameColor}' class='{FontSizes.FontSizeSm}'>{name}{check}</font>",
                    string.Empty,
                    (p, opt) =>
                    {
                        wcPlayer.SetLanguage(langCode);
                        Show(wcPlayer);
                    });
            }

            // Revenir aux paramètres par défaut du serveur
            builder.AddOption(
                $"<font color='#AAAAAA' class='{FontSizes.FontSizeSm}'>↺ {localizer["menu.settings.language.default"]}</font>",
                string.Empty,
                (p, opt) =>
                {
                    wcPlayer.SetLanguage(null);
                    Show(wcPlayer);
                });

            MenuManager.OpenMainMenu(wcPlayer.Player, builder.Build());
        }
    }
}
