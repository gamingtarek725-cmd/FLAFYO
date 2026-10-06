namespace Flafyo.Core;

public enum PackKind { Light, Heavy }

/// <summary>A downloadable offline translation pack. Light = OPUS-MT (small, fast). Heavy = NLLB-200 600M (larger, more accurate).</summary>
public sealed class LanguagePack
{
    private const string Marker = ".complete";

    public PackKind Kind { get; }
    public string ShortName { get; }
    public string Folder { get; }
    private readonly string _baseUrl;
    private readonly (string File, long Bytes)[] _files;

    private LanguagePack(PackKind kind, string shortName, string folder, string baseUrl, (string, long)[] files)
    { Kind = kind; ShortName = shortName; Folder = folder; _baseUrl = baseUrl; _files = files; }

    public static LanguagePack Light { get; } = new(PackKind.Light, "الخفيفة", "opus-mt-en-ar",
        "https://huggingface.co/Xenova/opus-mt-en-ar/resolve/63765349ac9f6c43f6e5bd8c0a5583224cfd8587/",
        new (string, long)[]
        {
            ("config.json", 1_400), ("generation_config.json", 300), ("source.spm", 801_000), ("target.spm", 917_000),
            ("vocab.json", 2_240_000), ("onnx/encoder_model_quantized.onnx", 51_800_000), ("onnx/decoder_model_quantized.onnx", 91_200_000),
        });

    public static LanguagePack Heavy { get; } = new(PackKind.Heavy, "التقيلة", "nllb-200-600m",
        "https://huggingface.co/Xenova/nllb-200-distilled-600M/resolve/bfaad393bfa9f83f73fb0ddaeb21f42d3323e821/",
        new (string, long)[]
        {
            ("config.json", 1_500), ("generation_config.json", 300), ("tokenizer.json", 17_300_000),
            ("onnx/encoder_model_quantized.onnx", 419_000_000), ("onnx/decoder_model_merged_quantized.onnx", 476_000_000),
        });

    public string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAFYO", "models", Folder);

    public long ApproxBytes => _files.Sum(f => f.Bytes);

    public bool IsInstalled() =>
        File.Exists(Path.Combine(Dir, Marker)) && _files.All(f => new FileInfo(Path.Combine(Dir, f.File)) is { Exists: true, Length: > 0 });

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Dir;
        Directory.CreateDirectory(Path.Combine(dir, "onnx"));
        long total = ApproxBytes, done = 0;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var (file, approx) in _files)
        {
            var target = Path.Combine(dir, file);
            if (File.Exists(target) && new FileInfo(target).Length > 0) { done += approx; progress?.Report((double)done / total); continue; }
            var part = target + ".part";
            using (var resp = await http.GetAsync(_baseUrl + file, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(part);
                var buf = new byte[81920]; int read; long fileBytes = 0;
                while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                    fileBytes += read;
                    progress?.Report(Math.Min(1.0, (double)(done + Math.Min(fileBytes, approx)) / total));
                }
            }
            File.Move(part, target, overwrite: true);
            done += approx;
        }
        File.WriteAllText(Path.Combine(dir, Marker), DateTime.UtcNow.ToString("O"));
        progress?.Report(1.0);
    }

    /// <summary>Loads the pack's model (slow: do it on a background thread, e.g. through <see cref="LazyTranslator"/>).</summary>
    public ITranslator CreateTranslator(int threads, int beams)
    {
        if (Kind == PackKind.Heavy) return OnnxNllbTranslator.Load(Dir, threads);
        var m = OnnxMarianTranslator.Load(Dir, threads);
        m.Beams = beams;
        return m;
    }
}
