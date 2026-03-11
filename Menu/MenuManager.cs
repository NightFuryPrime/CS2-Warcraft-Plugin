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

        WasdMenu wasdMenu = new(menu.Title, WarcraftPlugin.Instance)
        {
            MenuTime = 0,
            ExitButton = true
        };

        foreach (MenuOption option in menu.Options)
        {
            string display = option.OptionDisplay ?? string.Empty;
            if (!string.IsNullOrEmpty(option.SubOptionDisplay))
            {
                display = $"{display}<br>{option.SubOptionDisplay}";
            }

            ItemOption item = wasdMenu.AddItem(display, (p, _) =>
            {
                option.OnChoose?.Invoke(p, option);
            });

            item.PostSelectAction = PostSelectAction.Nothing;
        }

        if (wasdMenu.ItemOptions.Count == 0)
            return;

        wasdMenu.DisplayAt(player, selectedOptionIndex, 0);
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
