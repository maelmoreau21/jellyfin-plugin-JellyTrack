using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace JellyTrack.Plugin.Services;

public static class PluginLocalization
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public static string Translate(string key, string? language = null)
    {
        foreach (var candidate in GetLanguageFallbacks(language))
        {
            var values = LoadLanguage(candidate);
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return key;
    }

    public static string GetConfiguredLanguage()
    {
        var configured = global::JellyTrack.Plugin.Plugin.Instance?.Configuration?.PreferredLanguage;
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : CultureInfo.CurrentUICulture.Name;
    }

    private static IEnumerable<string> GetLanguageFallbacks(string? language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = Normalize(language);

        if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
        {
            yield return normalized;
        }

        var baseLanguage = normalized?.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(baseLanguage) && seen.Add(baseLanguage))
        {
            yield return baseLanguage;
        }

        if (seen.Add("en"))
        {
            yield return "en";
        }
    }

    private static IReadOnlyDictionary<string, string> LoadLanguage(string language)
    {
        var normalized = Normalize(language) ?? "en";
        return Cache.GetOrAdd(normalized, static lang =>
        {
            var resourceName = $"{typeof(global::JellyTrack.Plugin.Plugin).Namespace}.Localization.{lang}.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return Empty;
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? Empty;
            }
            catch (JsonException)
            {
                return Empty;
            }
        });
    }

    private static string? Normalize(string? language)
    {
        var trimmed = language?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed.ToLowerInvariant();
    }
}
