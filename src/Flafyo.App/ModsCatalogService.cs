using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Flafyo.Desktop;

internal sealed class ModsCatalogService
{
    private const string CatalogUrl = "https://raw.githubusercontent.com/gamingtarek725-cmd/FLAFYO/main/data/mods-library.json";
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
        public string Game { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Category { get; set; } = "";
        public string SourceName { get; set; } = "";
        public string SourceUrl { get; set; } = "";
        public string Version { get; set; } = "";
        public string Summary { get; set; } = "";
        public string InstallMethod { get; set; } = "";
        public string SafetyNotes { get; set; } = "";
        public string CoverUrl { get; set; } = "";
        public long? DownloadCount { get; set; }
        public string EntryType { get; set; } = "directory";
    }

    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FLAFYO", "mods-library.json");

    internal static Catalog? LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
            {
                var cached = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(CachePath), Options);
                if (cached != null) return NormalizeCatalog(cached);
            }

            // First-run offline fallback bundled with the application.
            using var stream = typeof(ModsCatalogService).Assembly
                .GetManifestResourceStream("Flafyo.Desktop.mods-library.json");
            if (stream == null) return null;
            var bundled = JsonSerializer.Deserialize<Catalog>(stream, Options);
            return bundled == null ? null : NormalizeCatalog(bundled);
        }
        catch { return null; }
    }

    private static Catalog NormalizeCatalog(Catalog catalog)
    {
        var allowed = new[] {
            "curseforge.com", "minecraft.net", "moddingway.com", "fifa-infinity.com", "steamcommunity.com"
        };
        catalog.Entries = (catalog.Entries ?? new List<Entry>())
            .Where(e => e is not null &&
                !string.IsNullOrWhiteSpace(e.Title) &&
                !string.IsNullOrWhiteSpace(e.SourceName) &&
                Uri.TryCreate(e.SourceUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                uri.IsDefaultPort &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                allowed.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                    uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
            .Select(e =>
            {
                e.Title = e.Title.Trim();
                e.Platform = (e.Platform ?? "").Trim();
                e.Category = (e.Category ?? "").Trim();
                e.EntryType = string.IsNullOrWhiteSpace(e.EntryType) ? "directory" : e.EntryType.Trim().ToLowerInvariant();
                return e;
            })
            .GroupBy(e => e.SourceUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Platform, StringComparer.OrdinalIgnoreCase)
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
                      ?? throw new InvalidDataException("Invalid mods catalog.");
        catalog = NormalizeCatalog(catalog);
        if (catalog.Entries.Count == 0)
            throw new InvalidDataException("Mods catalog contains no valid sources.");
        await Flafyo.Core.AtomicFile.WriteAllTextAsync(
            CachePath,
            JsonSerializer.Serialize(catalog, Options));
        return catalog;
    }
}
