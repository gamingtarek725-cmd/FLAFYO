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
            var cached = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(CachePath), Options);
            return cached == null ? null : NormalizeCatalog(cached);
        }
        catch { return null; }
    }

    private static Catalog NormalizeCatalog(Catalog catalog)
    {
        var allowed = new[] { "ta3reebat.com", "muarrab.com", "ta3reb.com" };
        catalog.Entries = (catalog.Entries ?? new List<Entry>())
            .Where(e =>
                e is not null &&
                !string.IsNullOrWhiteSpace(e.Title) &&
                !string.IsNullOrWhiteSpace(e.SourceName) &&
                Uri.TryCreate(e.SourceUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                uri.IsDefaultPort &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                allowed.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                    uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)) &&
                !uri.AbsolutePath.TrimEnd('/').EndsWith("/home", StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                try { e.Title = Uri.UnescapeDataString(e.Title.Replace("+", " ")); } catch { }
                e.Title = e.Title.Trim();
                e.UpdateType = string.IsNullOrWhiteSpace(e.UpdateType) ? "listing" : e.UpdateType.Trim().ToLowerInvariant();
                return e;
            })
            .Where(e => !e.Title.Equals("العربية", StringComparison.OrdinalIgnoreCase) &&
                        !e.Title.Equals("home", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(e => e.UpdateType.Equals("update", StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return catalog;
    }

    internal static async Task<Catalog> FetchAsync()
    {
        using var response = await Http.GetAsync(CatalogUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var catalog = JsonSerializer.Deserialize<Catalog>(json, Options)
                      ?? throw new InvalidDataException("Invalid localization catalog.");
        catalog = NormalizeCatalog(catalog);
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(catalog, Options));
        return catalog;
    }
}