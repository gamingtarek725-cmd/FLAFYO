namespace Flafyo.Core;

/// <summary>Dictionary → cache → (idiom rewrite → translation model), in that order.</summary>
public sealed class TranslationResolver
{
    private readonly IReadOnlyDictionary<string, string> _dictionary;
    private readonly TranslationCache _cache;
    private readonly ITranslator _translator;
    private readonly IdiomRewriter? _idioms;

    public TranslationResolver(ITranslator translator, TranslationCache cache,
        IReadOnlyDictionary<string, string>? dictionary = null, IdiomRewriter? idioms = null)
    {
        _translator = translator;
        _cache = cache;
        _idioms = idioms;
        _dictionary = new Dictionary<string, string>(dictionary ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(string Text, bool Instant)> ResolveAsync(string normalized, CancellationToken ct)
    {
        if (_dictionary.TryGetValue(normalized, out var d)) return (d, true);
        if (_cache.TryGet(normalized, out var c)) return (c, true);
        var input = _idioms?.Rewrite(normalized) ?? normalized;      // idioms become plain meaning before translation
        var t = await _translator.TranslateAsync(input, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(t)) _cache.Set(normalized, t); // cached under the ORIGINAL text; never cache empty output
        return (t, false);
    }
}
