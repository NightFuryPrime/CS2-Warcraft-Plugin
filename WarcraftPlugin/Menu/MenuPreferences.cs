using System;
using System.Net;
using System.Text.RegularExpressions;

namespace WarcraftPlugin.Menu;

internal enum WarcraftMenuScale
{
    Small,
    Normal,
    Large,
    ExtraLarge
}

internal enum WarcraftMenuType
{
    WasdMenu,
    CenterHtmlMenu,
    ChatMenu,
    ConsoleMenu
}

internal readonly record struct MenuPreference(WarcraftMenuScale Scale, WarcraftMenuType Type);

internal sealed class StoredMenuPreference
{
    public string MenuScale { get; set; } = string.Empty;
    public string MenuType { get; set; } = string.Empty;
}

internal static class MenuPreferenceResolver
{
    internal static readonly MenuPreference SafeDefault = new(WarcraftMenuScale.Large, WarcraftMenuType.WasdMenu);

    internal static MenuPreference Resolve(Config config, StoredMenuPreference stored = null)
    {
        var fallback = FromConfig(config);
        if (stored == null)
            return fallback;

        return new MenuPreference(
            ParseScale(stored.MenuScale, fallback.Scale),
            ParseMenuType(stored.MenuType, fallback.Type));
    }

    internal static MenuPreference FromConfig(Config config)
    {
        if (config == null)
            return SafeDefault;

        return new MenuPreference(
            ParseScale(config.DefaultMenuScale, SafeDefault.Scale),
            ParseMenuType(config.DefaultWarcraftMenuType, SafeDefault.Type));
    }

    internal static void NormalizeConfig(Config config)
    {
        if (config == null)
            return;

        var preference = FromConfig(config);
        config.DefaultMenuScale = preference.Scale.ToString();
        config.DefaultWarcraftMenuType = preference.Type.ToString();
    }

    internal static WarcraftMenuScale ParseScale(string value, WarcraftMenuScale fallback)
    {
        return Enum.TryParse(value, ignoreCase: true, out WarcraftMenuScale parsed)
            ? parsed
            : fallback;
    }

    internal static WarcraftMenuType ParseMenuType(string value, WarcraftMenuType fallback)
    {
        return Enum.TryParse(value, ignoreCase: true, out WarcraftMenuType parsed)
            ? parsed
            : fallback;
    }
}

internal static partial class MenuText
{
    private static readonly string[] FontClasses =
    [
        "fontSize-xs",
        "fontSize-s",
        "fontSize-sm",
        "fontSize-m",
        "fontSize-l",
        "fontSize-xl",
        "fontSize-xxl"
    ];

    internal static string ApplyScale(string html, WarcraftMenuScale scale)
    {
        if (string.IsNullOrEmpty(html))
            return html ?? string.Empty;

        return FontClassRegex().Replace(html, match =>
        {
            var quote = match.Groups["quote"].Value;
            var current = match.Groups["class"].Value;
            var index = Array.IndexOf(FontClasses, current);
            if (index < 0)
                return match.Value;

            var scaledIndex = Math.Clamp(index + Offset(scale), 0, FontClasses.Length - 1);
            return $"class={quote}{FontClasses[scaledIndex]}{quote}";
        });
    }

    internal static string FooterFontClass(WarcraftMenuScale scale)
    {
        var baseIndex = Array.IndexOf(FontClasses, FontSizes.FontSizeS);
        return FontClasses[Math.Clamp(baseIndex + Offset(scale), 0, FontClasses.Length - 1)];
    }

    internal static string ToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var withBreaks = BreakRegex().Replace(html, " - ");
        var withoutTags = TagRegex().Replace(withBreaks, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static int Offset(WarcraftMenuScale scale) => scale switch
    {
        WarcraftMenuScale.Small => -1,
        WarcraftMenuScale.Normal => 0,
        WarcraftMenuScale.Large => 1,
        WarcraftMenuScale.ExtraLarge => 2,
        _ => 1
    };

    [GeneratedRegex("class=(?<quote>['\"])(?<class>fontSize-[a-z]+)\\k<quote>", RegexOptions.IgnoreCase)]
    private static partial Regex FontClassRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();
}
