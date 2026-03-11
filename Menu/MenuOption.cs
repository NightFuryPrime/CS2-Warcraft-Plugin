using CounterStrikeSharp.API.Core;
using System;

namespace WarcraftPlugin.Menu;

internal sealed class MenuOption
{
    internal string OptionDisplay { get; set; } = string.Empty;
    internal string SubOptionDisplay { get; set; } = string.Empty;
    internal Action<CCSPlayerController, MenuOption> OnChoose { get; set; }
    internal int Index { get; set; }
    internal Action<CCSPlayerController, MenuOption> OnSelect { get; set; }
}
