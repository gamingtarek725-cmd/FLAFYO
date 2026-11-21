using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Flafyo.Desktop;

internal sealed class LocalizationCatalogService
{
    private const string CatalogUrl = "https://raw.githubusercontent.com/gamingtarek725-cmd/FLAFYO/main/data/localization-library.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal sealed class Catalog
    {
        public string GeneratedAt { get; set; } = "";
        public List<Entry> Entries { get; set; } = new();
    }

    internal sealed class Entry
    {
        public string Title { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public string SourceName { get; set; } = "";
        public string Platform { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string TranslationVersion { get; set; } = "";
        public string UpdateType { get; set; } = "";
        public string Summary { get; set; } = "";
        public string PublishedAt { get; set; } = "";
    }

    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FLAFYO", "localization-library.json");

    internal static Catalog? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            return JsonSerializer.Deserialize<Catalog>(File.ReadAllText(CachePath), Options);
        }
        catch { return null; }
    }

    internal static async Task<Catalog> FetchAsync()
    {
        using var response = await Http.GetAsync(CatalogUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var catalog = JsonSerializer.Deserialize<Catalog>(json, Options)
                      ?? throw new InvalidDataException("Invalid localization catalog.");
        var allowed = new[] { "ta3reebat.com", "muarrab.com", "ta3reb.com" };
        catalog.Entries = catalog.Entries.Where(e =>
            Uri.TryCreate(e.SourceUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            allowed.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(e => e.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, json);
        return catalog;
    }
}