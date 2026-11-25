using CounterStrikeSharp.API.Core;
using System;
using System.Collections.Generic;

namespace WarcraftPlugin.Menu;

internal sealed class Menu
{
    internal string Title { get; }
    internal int ResultsBeforePaging { get; }
    internal List<MenuOption> Options { get; } = [];

    internal Menu(string title, int resultsBeforePaging)
    {
        Title = title;
        ResultsBeforePaging = resultsBeforePaging;
    }

    internal MenuOption Add(string display, string subDisplay, Action<CCSPlayerController, MenuOption> onChoice, Action<CCSPlayerController, MenuOption> onSelect = null)
    {
        MenuOption option = new()
        {
            OptionDisplay = display ?? string.Empty,
            SubOptionDisplay = subDisplay ?? string.Empty,
            OnChoose = onChoice,
            OnSelect = onSelect,
            Index = Options.Count
        };

        Options.Add(option);
        return option;
    }
}
