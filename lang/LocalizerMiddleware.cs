using Microsoft.Extensions.Localization;
using System.Linq;
using System.Collections.Generic;
using System;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.RegularExpressions;
using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace WarcraftPlugin.lang
{
    public static class LocalizerMiddleware
    {
        private static readonly ConcurrentDictionary<string, WarcraftLocalizer> _langCache = new(StringComparer.OrdinalIgnoreCase);
        private static string _moduleDirectory;

        internal static void InvalidateCache() => _langCache.Clear();

        /// <summary>Creates a per-player localizer for the given language code, cached per language.</summary>
        internal static WarcraftLocalizer CreateForLanguage(string moduleDirectory, string languageCode)
        {
            _moduleDirectory ??= moduleDirectory;
            return _langCache.GetOrAdd(languageCode, code =>
            {
                var chatColors = GetChatColors();
                var strings = new List<LocalizedString>();
                var langDir = Path.Combine(moduleDirectory, "lang");
                var targetFile = Path.Combine(langDir, $"{code}.json");
                var fallbackFile = Path.Combine(langDir, "en.json");

                if (File.Exists(targetFile))
                    LoadJsonInto(strings, targetFile, chatColors);

                // Add fallback keys from en.json for anything missing
                if (File.Exists(fallbackFile))
                    LoadJsonInto(strings, fallbackFile, chatColors);

                var unique = strings
                    .GroupBy(s => s.Name)
                    .Select(g => g.First())
                    .ToList();

                return new WarcraftLocalizer(unique);
            });
        }

        private static void LoadJsonInto(List<LocalizedString> strings, string file, Dictionary<string, string> chatColors)
        {
            try
            {
                var json = File.ReadAllText(file);
                var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                using var doc = JsonDocument.Parse(json, opts);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        strings.Add(new LocalizedString(prop.Name.ToLower(), ReplaceChatColors(prop.Value.GetString() ?? string.Empty, chatColors)));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warcraft] Failed to load lang file {file}: {ex.Message}");
            }
        }

        /// <param name="languageCode">Optional language code (e.g. "en", "fr", "de"). If null, uses CurrentUICulture.</param>
        internal static IStringLocalizer Load(IStringLocalizer localizer, string moduleDirectory, string languageCode = null)
        {
            _moduleDirectory = moduleDirectory;
            var chatColors = GetChatColors();

            List<LocalizedString> customHeroLocalizerStrings = LoadCustomHeroLocalizations(moduleDirectory, chatColors, languageCode);

            // Process the localizer strings
            var localizedStrings = localizer.GetAllStrings()
                .Select(ls => new LocalizedString(ls.Name, ReplaceChatColors(ls.Value, chatColors)))
                .Concat(customHeroLocalizerStrings)
                .ToList();

            return new WarcraftLocalizer(localizedStrings);
        }

        private static List<LocalizedString> LoadCustomHeroLocalizations(string moduleDirectory, Dictionary<string, string> chatColors, string languageCode = null)
        {
            var culture = !string.IsNullOrWhiteSpace(languageCode)
                ? CultureInfo.GetCultureInfo(languageCode.Trim())
                : CultureInfo.CurrentUICulture;
            var twoLetter = culture.TwoLetterISOLanguageName;
            var searchPattern = $"*.{twoLetter}*.json";
            var fallbackSearchPattern = "*.en*.json";

            var customHeroLocalizations = Directory.EnumerateFiles(Path.Combine(moduleDirectory, "lang"), searchPattern);
            var fallbackLocalizations = Directory.EnumerateFiles(Path.Combine(moduleDirectory, "lang"), fallbackSearchPattern);

            // Use a thread-safe collection for parallel processing
            var concurrentLocalizerStrings = new ConcurrentBag<LocalizedString>();

            var jsonOptions = new JsonSerializerOptions { AllowTrailingCommas = true };

            Parallel.ForEach(customHeroLocalizations.Concat(fallbackLocalizations), file =>
            {
                var jsonContent = File.ReadAllText(file);
                var customHeroLocalizations = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, jsonOptions);

                if (customHeroLocalizations != null)
                {
                    foreach (var localization in customHeroLocalizations)
                    {
                        concurrentLocalizerStrings.Add(new LocalizedString(localization.Key, ReplaceChatColors(localization.Value, chatColors), false, searchedLocation: file));
                    }
                }
            });

            // Use English as fallback
            var uniqueLocalizerStrings = concurrentLocalizerStrings
                .GroupBy(ls => ls.Name)
                .Select(g => g.FirstOrDefault(ls => !ls.SearchedLocation.Contains(".en.")) ?? g.First())
                .ToList();

            return uniqueLocalizerStrings;
        }

        private static Dictionary<string, string> GetChatColors()
        {
            return typeof(ChatColors).GetProperties()
                .ToDictionary(prop => prop.Name.ToLower(), prop => prop.GetValue(null)?.ToString() ?? string.Empty, StringComparer.InvariantCultureIgnoreCase);
        }

        private static readonly Regex ChatColorRegex = new Regex(@"{(\D+?)}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string ReplaceChatColors(string input, Dictionary<string, string> chatColors)
        {
            return ChatColorRegex.Replace(input, match =>
            {
                var key = match.Groups[1].Value.ToLower();
                return chatColors.TryGetValue(key, out var value) ? value : "{UNKNOWN-COLOR}";
            });
        }
    }

    public class WarcraftLocalizer(List<LocalizedString> localizedStrings) : IStringLocalizer
    {
        private readonly List<LocalizedString> _localizedStrings = localizedStrings;

        public LocalizedString this[string name] => _localizedStrings.FirstOrDefault(ls => ls.Name == name.ToLower()) ?? new LocalizedString(name.ToLower(), name.ToLower());

        public LocalizedString this[string name, params object[] arguments] =>
            new(name.ToLower(), string.Format(_localizedStrings.FirstOrDefault(ls => ls.Name == name.ToLower())?.Value ?? name.ToLower(), arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _localizedStrings;

        public IStringLocalizer WithCulture(CultureInfo culture) => this;
    }

    public static class StringLocalizerExtensions
    {
        public static bool Exists(this IStringLocalizer localizer, string name)
        {
            return localizer.GetAllStrings().Any(ls => ls.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }
    }
}
