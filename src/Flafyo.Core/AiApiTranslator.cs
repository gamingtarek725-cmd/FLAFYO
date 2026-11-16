using System.Text;
using System.Text.Json;

namespace Flafyo.Core;

public enum AiProvider { OpenAiCompatible, Anthropic }

/// <summary>
/// Translates using the person's OWN account on an AI service (their API key), instead of FLAFYO's built-in models.
/// "OpenAiCompatible" works with any service that implements the OpenAI chat-completions API shape (OpenAI itself,
/// and most OpenAI-compatible providers). "Anthropic" calls the Claude Messages API directly.
/// The API key is used only to call that provider's endpoint directly from this PC; FLAFYO never sees or stores it anywhere else.
/// </summary>
public sealed class AiApiTranslator : ITranslator, IDisposable
{
    private const string SystemPrompt =
        "You translate video game text from English to Arabic. Reply with ONLY the Arabic translation, " +
        "no quotes, no notes, no explanation. Keep any placeholders like {0}, %s, or [name] exactly as they are.";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(40) };
    private readonly AiProvider _provider;
    private readonly string _apiKey, _model, _baseUrl;

    public AiApiTranslator(AiProvider provider, string apiKey, string model, string? baseUrl = null)
    {
        _provider = provider; _apiKey = apiKey; _model = model;
        _baseUrl = (baseUrl?.TrimEnd('/')) is { Length: > 0 } b ? b
            : provider == AiProvider.Anthropic ? "https://api.anthropic.com" : "https://api.openai.com/v1";
    }

    public async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var (url, json, apply) = _provider == AiProvider.Anthropic
            ? (_baseUrl + "/v1/messages", BuildAnthropicRequest(_model, text), (Action<HttpRequestMessage>)(r =>
              {
                  r.Headers.Add("x-api-key", _apiKey);
                  r.Headers.Add("anthropic-version", "2023-06-01");
              }))
            : (_baseUrl + "/chat/completions", BuildOpenAiRequest(_model, text), r =>
                r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey));

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        apply(req);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(ExtractApiError(body) ?? $"HTTP {(int)resp.StatusCode}");
        var text2 = _provider == AiProvider.Anthropic ? ParseAnthropicResponse(body) : ParseOpenAiResponse(body);
        return text2.Trim().Trim('"');
    }

    public static string BuildOpenAiRequest(string model, string userText) => JsonSerializer.Serialize(new
    {
        model,
        temperature = 0.2,
        messages = new object[] { new { role = "system", content = SystemPrompt }, new { role = "user", content = userText } },
    });

    public static string ParseOpenAiResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    public static string BuildAnthropicRequest(string model, string userText) => JsonSerializer.Serialize(new
    {
        model,
        max_tokens = 1024,
        system = SystemPrompt,
        messages = new object[] { new { role = "user", content = userText } },
    });

    public static string ParseAnthropicResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private static string? ExtractApiError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.ValueKind == JsonValueKind.String ? e.GetString() : e.TryGetProperty("message", out var m) ? m.GetString() : body;
        }
        catch { }
        return body.Length > 200 ? body[..200] : body;
    }

    public void Dispose() => _http.Dispose();
}
