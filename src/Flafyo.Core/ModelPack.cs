namespace Flafyo.Core;

/// <summary>Downloads/validates the offline English→Arabic language pack (OPUS-MT, ~145 MB, pinned revision).</summary>
public static class ModelPack
{
    private const string Base = "https://huggingface.co/Xenova/opus-mt-en-ar/resolve/63765349ac9f6c43f6e5bd8c0a5583224cfd8587/";
    private const string Marker = ".complete";

    private static readonly (string File, long ApproxBytes)[] Files =
    {
        ("config.json", 1_400), ("generation_config.json", 300), ("source.spm", 801_000), ("target.spm", 917_000),
        ("vocab.json", 2_240_000), ("onnx/encoder_model_quantized.onnx", 51_800_000), ("onnx/decoder_model_quantized.onnx", 91_200_000),
    };

    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAFYO", "models", "opus-mt-en-ar");

    public static bool IsInstalled(string dir) =>
        File.Exists(Path.Combine(dir, Marker)) && Files.All(f => new FileInfo(Path.Combine(dir, f.File)) is { Exists: true, Length: > 0 });

    public static async Task DownloadAsync(string dir, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(dir, "onnx"));
        long total = Files.Sum(f => f.ApproxBytes), done = 0;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var (file, approx) in Files)
        {
            var target = Path.Combine(dir, file);
            if (File.Exists(target) && new FileInfo(target).Length > 0) { done += approx; progress?.Report((double)done / total); continue; }
            var part = target + ".part";
            using (var resp = await http.GetAsync(Base + file, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
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
}
