using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Menu.WarcraftMenu
{
    internal static class AdminMenu
    {
        internal static void Show(CCSPlayerController admin)
        {
            if (!AdminManager.PlayerHasPermissions(admin, "@css/admin"))
            {
                admin.PrintToChat($" {ChatColors.Red}{WarcraftPlugin.Instance.Localizer["menu.admin.no.permission"]}");
                return;
            }

            var plugin = WarcraftPlugin.Instance;

            var players = Utilities.GetPlayers()
                .Where(p => p.IsValid && !p.IsBot)
                .OrderBy(p => p.GetRealPlayerName())
                .ToList();

            var builder = MenuBuilder.Create(
                $"<font color='#FF4444' class='{FontSizes.FontSizeM}'>⚙ {plugin.Localizer["menu.admin"]}</font><br>" +
                $"<font color='#AAAAAA' class='{FontSizes.FontSizeS}'>{plugin.Localizer["menu.admin.select.player"]}</font>", 5);

            foreach (var target in players)
            {
                var wcTarget = target.GetWarcraftPlayer();
                var levelInfo = wcTarget != null
                    ? $" <font color='gold' class='{FontSizes.FontSizeS}'>Niv.{wcTarget.GetLevel()}</font>"
                    : string.Empty;
                var classInfo = wcTarget?.GetClass()?.LocalizedDisplayName ?? "?";
                var isSelf = target == admin;
                var nameColor = isSelf ? "#AAAAAA" : "white";

                var targetCapture = target;
                builder.AddOption(
                    $"<font color='{nameColor}' class='{FontSizes.FontSizeSm}'>{target.GetRealPlayerName()}{levelInfo}</font>",
                    $"<font color='#888888' class='{FontSizes.FontSizeS}'>{classInfo}</font>",
                    (p, opt) => ShowPlayerActions(admin, targetCapture));
            }

            MenuManager.OpenMainMenu(admin, builder.Build());
        }

        private static void ShowPlayerActions(CCSPlayerController admin, CCSPlayerController target)
        {
            var plugin = WarcraftPlugin.Instance;
            var wcTarget = target.GetWarcraftPlayer();

            if (wcTarget == null)
            {
                admin.PrintToChat($" {ChatColors.Red}Joueur introuvable.");
                return;
            }

            var classColor = wcTarget.GetClass()?.DefaultColor.AdjustBrightness(1.3f).ToHex() ?? "#FFFFFF";

            var builder = MenuBuilder.Create(
                $"<font color='#FF4444' class='{FontSizes.FontSizeM}'>⚙ {target.GetRealPlayerName()}</font><br>" +
                $"<font color='{classColor}' class='{FontSizes.FontSizeS}'>{wcTarget.GetClass()?.LocalizedDisplayName ?? "?"}</font>" +
                $"<font color='gold' class='{FontSizes.FontSizeS}'> - {plugin.Localizer["menu.skills.level"]} {wcTarget.GetLevel()}</font>", 5);

            // Donner XP
            foreach (var amount in new[] { 100, 500, 1000, 5000 })
            {
                var xpAmount = amount;
                builder.AddOption(
                    $"<font color='#90EE90' class='{FontSizes.FontSizeSm}'>+{xpAmount} XP</font>",
                    string.Empty,
                    (p, opt) =>
                    {
                        if (!target.IsValid)
                        {
                            p.PrintToChat($" {ChatColors.Red}Joueur déconnecté.");
                            Show(admin);
                            return;
                        }
                        plugin.XpSystem.AddXp(target, xpAmount);
                        p.PrintToChat($" {ChatColors.Green}+{xpAmount} XP {ChatColors.Default}→ {target.GetRealPlayerName()}");
                        ShowPlayerActions(admin, target);
                    });
            }

            // Réinitialiser les skills
            builder.AddOption(
                $"<font color='orange' class='{FontSizes.FontSizeSm}'>↺ {plugin.Localizer["menu.admin.reset.skills"]}</font>",
                $"<font color='#888888' class='{FontSizes.FontSizeS}'>{plugin.Localizer["menu.admin.reset.skills.desc"]}</font>",
                (p, opt) =>
                {
                    if (!target.IsValid) { Show(admin); return; }
                    var abilityCount = wcTarget.GetClass().Abilities.Count;
                    for (int i = 0; i < abilityCount; i++)
                        wcTarget.SetAbilityLevel(i, 0);
                    p.PrintToChat($" {ChatColors.Orange}{plugin.Localizer["menu.admin.reset.skills.done", target.GetRealPlayerName()]}");
                    ShowPlayerActions(admin, target);
                });

            // Réinitialisation complète
            builder.AddOption(
                $"<font color='#FF4444' class='{FontSizes.FontSizeSm}'>✖ {plugin.Localizer["menu.admin.factory.reset"]}</font>",
                $"<font color='#888888' class='{FontSizes.FontSizeS}'>{plugin.Localizer["menu.admin.factory.reset.desc"]}</font>",
                (p, opt) =>
                {
                    if (!target.IsValid) { Show(admin); return; }
                    wcTarget.currentLevel = 0;
                    wcTarget.currentXp = 0;
                    var abilityCount = wcTarget.GetClass().Abilities.Count;
                    for (int i = 0; i < abilityCount; i++)
                        wcTarget.SetAbilityLevel(i, 0);
                    if (target.PawnIsAlive)
                        target.PlayerPawn.Value?.CommitSuicide(false, false);
                    p.PrintToChat($" {ChatColors.Red}{plugin.Localizer["menu.admin.factory.reset.done", target.GetRealPlayerName()]}");
                    Show(admin);
                });

            // Retour
            builder.AddOption(
                $"<font color='#00BFFF' class='{FontSizes.FontSizeSm}'>◀ {plugin.Localizer["menu.admin.back"]}</font>",
                string.Empty,
                (p, opt) => Show(admin));

            MenuManager.OpenMainMenu(admin, builder.Build());
        }
    }
}
