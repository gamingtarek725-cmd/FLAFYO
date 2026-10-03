using System.Text.Json;

namespace Flafyo.Core;

/// <summary>
/// TEMPORARY translator (needs internet) using the free MyMemory API, so the app works end-to-end today.
/// It will be replaced by an offline model in a later phase. Texts are cut to 450 chars (API limit).
/// </summary>
public sealed class OnlineTranslator : ITranslator, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly string _pair;

    public OnlineTranslator(string from = "en", string to = "ar") => _pair = $"{from}|{to}";

    public async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        if (text.Length > 450) text = text[..450];
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(_pair)}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseMyMemory(json);
    }

    public static string ParseMyMemory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int status = 200;
        if (root.TryGetProperty("responseStatus", out var s))
            status = s.ValueKind == JsonValueKind.Number ? s.GetInt32() : int.TryParse(s.GetString(), out var n) ? n : 0;
        if (status != 200) throw new InvalidOperationException($"Translation service returned status {status}");
        var text = root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";
        if (text.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Translation service daily limit reached");
        return text;
    }

    public void Dispose() => _http.Dispose();
}
