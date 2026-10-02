namespace Flafyo.Core;

/// <summary>Dictionary → cache → translation model, in that order.</summary>
public sealed class TranslationResolver
{
    private readonly IReadOnlyDictionary<string, string> _dictionary;
    private readonly TranslationCache _cache;
    private readonly ITranslator _translator;

    public TranslationResolver(ITranslator translator, TranslationCache cache, IReadOnlyDictionary<string, string>? dictionary = null)
    {
        _translator = translator;
        _cache = cache;
        _dictionary = new Dictionary<string, string>(dictionary ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(string Text, bool Instant)> ResolveAsync(string normalized, CancellationToken ct)
    {
        if (_dictionary.TryGetValue(normalized, out var d)) return (d, true);
        if (_cache.TryGet(normalized, out var c)) return (c, true);
        var t = await _translator.TranslateAsync(normalized, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(t)) _cache.Set(normalized, t); // never cache empty/failed output
        return (t, false);
    }
}
