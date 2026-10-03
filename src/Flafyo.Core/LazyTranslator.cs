namespace Flafyo.Core;

/// <summary>Loads a heavy translator in the background; requests wait for it (the game never does). Falls back if loading fails.</summary>
public sealed class LazyTranslator : ITranslator, IDisposable
{
    private readonly Task<ITranslator> _load;

    public LazyTranslator(Func<ITranslator> factory, Func<ITranslator>? fallback = null, Action<string>? log = null)
    {
        _load = Task.Run(() =>
        {
            try { return factory(); }
            catch (Exception ex)
            {
                log?.Invoke("Offline model failed to load: " + ex.Message);
                if (fallback == null) throw;
                return fallback();
            }
        });
    }

    public async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        var tr = await _load.WaitAsync(ct).ConfigureAwait(false);
        return await tr.TranslateAsync(text, ct).ConfigureAwait(false);
    }

    public void Dispose() =>
        _load.ContinueWith(t => (t.Result as IDisposable)?.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
}
