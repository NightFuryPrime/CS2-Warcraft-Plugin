using CounterStrikeSharp.API.Core;
using CS2MenuManager.API.Class;
using CS2MenuManager.API.Enum;
using CS2MenuManager.API.Menu;
using MenuApi = CS2MenuManager.API.Class.MenuManager;

namespace WarcraftPlugin.Menu;

internal static class MenuManager
{
    internal static void OpenMainMenu(CCSPlayerController player, Menu menu, int selectedOptionIndex = 0)
    {
        if (player == null || menu == null)
            return;

        var plugin = WarcraftPlugin.Instance;
        var preference = plugin.GetMenuPreference(player);
        var title = MenuText.ApplyScale(menu.Title, preference.Scale);
        BaseMenu renderedMenu = MenuApi.MenuByType(preference.Type.ToString(), title, plugin);
        renderedMenu.MenuTime = 0;
        renderedMenu.ExitButton = true;

        foreach (MenuOption option in menu.Options)
        {
            string display = option.OptionDisplay ?? string.Empty;
            if (!string.IsNullOrEmpty(option.SubOptionDisplay))
            {
                display = $"{display}<br>{option.SubOptionDisplay}";
            }

            display = MenuText.ApplyScale(display, preference.Scale);
            if (preference.Type is WarcraftMenuType.ChatMenu or WarcraftMenuType.ConsoleMenu)
                display = MenuText.ToPlainText(display);

            ItemOption item = renderedMenu.AddItem(display, (p, _) =>
            {
                option.OnChoose?.Invoke(p, option);
            });

            item.PostSelectAction = PostSelectAction.Nothing;
        }

        if (renderedMenu.ItemOptions.Count == 0)
            return;

        renderedMenu.DisplayAt(player, selectedOptionIndex, 0);
    }

    internal static void CloseMenu(CCSPlayerController player)
    {
        if (player == null)
            return;

        MenuApi.CloseActiveMenu(player);
    }

    internal static Menu CreateMenu(string title = "", int resultsBeforePaging = 4)
    {
        return new Menu(title, resultsBeforePaging);
    }
}
