using CounterStrikeSharp.API.Core;
using System;
using System.Collections.Generic;

namespace WarcraftPlugin.Menu;

internal sealed class MenuBuilder
{
    private readonly Menu _menu;

    private MenuBuilder(string title, int resultsBeforePaging)
    {
        _menu = MenuManager.CreateMenu(title, resultsBeforePaging);
    }

    internal static MenuBuilder Create(string title, int resultsBeforePaging = 4)
    {
        return new MenuBuilder(title, resultsBeforePaging);
    }

    internal MenuBuilder AddOption(string display, string subDisplay, Action<CCSPlayerController, MenuOption> onChoose, Action<CCSPlayerController, MenuOption> onSelect = null)
    {
        _menu.Add(display, subDisplay, onChoose, onSelect);
        return this;
    }

    internal MenuBuilder AddOptions<T>(
        IEnumerable<T> items,
        Func<T, string> displayFactory,
        Func<T, string> subDisplayFactory,
        Action<CCSPlayerController, MenuOption, T> onChoose,
        Action<CCSPlayerController, MenuOption, T> onSelect = null)
    {
        foreach (var item in items)
        {
            var current = item;
            _menu.Add(
                displayFactory?.Invoke(current),
                subDisplayFactory?.Invoke(current),
                (player, option) => onChoose?.Invoke(player, option, current),
                onSelect != null ? (player, option) => onSelect(player, option, current) : null
            );
        }

        return this;
    }

    internal Menu Build() => _menu;
}
