using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace WarcraftPlugin.Menu.WarcraftMenu;

internal static class MenuSettingsMenu
{
    internal static void Show(CCSPlayerController player)
    {
        if (player == null || !player.IsValid)
            return;

        var plugin = WarcraftPlugin.Instance;
        var preference = plugin.GetMenuPreference(player);
        var builder = MenuBuilder.Create(
            $"<font color='lightgrey' class='{FontSizes.FontSizeM}'>Warcraft Menu</font><br>" +
            $"<font color='grey' class='{FontSizes.FontSizeS}'>Size: {preference.Scale} | Type: {DisplayType(preference.Type)}</font>",
            5);

        foreach (WarcraftMenuScale scale in Enum.GetValues<WarcraftMenuScale>())
        {
            var current = scale;
            var selected = preference.Scale == current ? $"{ChatColors.Green}*" : string.Empty;
            builder.AddOption(
                $"<font color='white' class='{FontSizes.FontSizeSm}'>Size: {current} {selected}</font>",
                null,
                (p, _) =>
                {
                    SaveAndReopen(p, new MenuPreference(current, plugin.GetMenuPreference(p).Type));
                });
        }

        foreach (WarcraftMenuType type in Enum.GetValues<WarcraftMenuType>())
        {
            var current = type;
            var selected = preference.Type == current ? $"{ChatColors.Green}*" : string.Empty;
            builder.AddOption(
                $"<font color='white' class='{FontSizes.FontSizeSm}'>Type: {DisplayType(current)} {selected}</font>",
                null,
                (p, _) =>
                {
                    SaveAndReopen(p, new MenuPreference(plugin.GetMenuPreference(p).Scale, current));
                });
        }

        builder.AddOption(
            $"<font color='lightgreen' class='{FontSizes.FontSizeSm}'>Preview current settings</font>",
            $"<font color='#D3D3D3' class='{FontSizes.FontSizeS}'>Opens a sample menu using your current display settings.</font>",
            (p, _) => ShowPreview(p));

        MenuManager.OpenMainMenu(player, builder.Build());
    }

    private static void SaveAndReopen(CCSPlayerController player, MenuPreference preference)
    {
        WarcraftPlugin.Instance.SaveMenuPreference(player, preference);
        player.PrintToChat($" {ChatColors.Green}Warcraft menu updated: {preference.Scale}, {DisplayType(preference.Type)}.");
        Show(player);
    }

    private static void ShowPreview(CCSPlayerController player)
    {
        var preference = WarcraftPlugin.Instance.GetMenuPreference(player);
        var builder = MenuBuilder.Create(
            $"<font color='lightgrey' class='{FontSizes.FontSizeM}'>Warcraft Menu Preview</font><br>" +
            $"<font color='grey' class='{FontSizes.FontSizeS}'>{preference.Scale} | {DisplayType(preference.Type)}</font>",
            4);

        builder.AddOption(
            $"<font color='gold' class='{FontSizes.FontSizeSm}'>Race menu sample</font>",
            $"<font color='#D3D3D3' class='{FontSizes.FontSizeS}'>This is how descriptions will look.</font>",
            (p, _) => Show(p));

        builder.AddOption(
            $"<font color='lightgreen' class='{FontSizes.FontSizeSm}'>Shop item sample - $3000</font>",
            $"<font color='#D3D3D3' class='{FontSizes.FontSizeS}'>This is how item text will look.</font>",
            (p, _) => Show(p));

        builder.AddOption(
            $"<font color='cyan' class='{FontSizes.FontSizeSm}'>Back to menu settings</font>",
            null,
            (p, _) => Show(p));

        MenuManager.OpenMainMenu(player, builder.Build());
    }

    private static string DisplayType(WarcraftMenuType type) => type switch
    {
        WarcraftMenuType.CenterHtmlMenu => "Center HTML",
        WarcraftMenuType.ChatMenu => "Chat",
        WarcraftMenuType.ConsoleMenu => "Console",
        _ => "WASD"
    };
}
