namespace Flafyo.Core;

/// <summary>Loads a heavy translator in the background; requests wait for it (the game never does). Falls back if loading fails, and says so.</summary>
public sealed class LazyTranslator : ITranslator, IDisposable
{
    private readonly Task<ITranslator> _load;

    /// <summary>"loading", "offline", "online-fallback" or "failed".</summary>
    public string Engine { get; private set; } = "loading";
    public string? Error { get; private set; }
    public event Action<string, string?>? EngineReady;

    public LazyTranslator(Func<ITranslator> factory, Func<ITranslator>? fallback = null, Action<string>? log = null)
    {
        _load = Task.Run(() =>
        {
            try
            {
                var t = factory();
                Engine = "offline";
                EngineReady?.Invoke(Engine, null);
                return t;
            }
            catch (Exception ex)
            {
                var msg = ex.GetType().Name + ": " + ex.Message;
                Error = msg.Length > 160 ? msg[..160] : msg;
                log?.Invoke("Offline model failed to load: " + Error);
                if (fallback == null) { Engine = "failed"; EngineReady?.Invoke(Engine, Error); throw; }
                var f = fallback();
                Engine = "online-fallback";
                EngineReady?.Invoke(Engine, Error);
                return f;
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
