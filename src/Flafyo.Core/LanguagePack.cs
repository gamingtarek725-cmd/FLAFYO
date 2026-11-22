using System.Security.Cryptography;

namespace Flafyo.Core;

public enum PackKind { Light, Heavy, Madlad }

/// <summary>Optional downloadable offline translation packs. MADLAD is the largest and slowest model.</summary>
public sealed class LanguagePack
{
    private const string Marker = ".complete";

    private sealed record ModelFile(string File, long Bytes, string? Sha256 = null, bool ExactLength = false);

    public PackKind Kind { get; }
    public string ShortName { get; }
    public string Folder { get; }
    private readonly string _baseUrl;
    private readonly ModelFile[] _files;

    private LanguagePack(PackKind kind, string shortName, string folder, string baseUrl, ModelFile[] files)
    { Kind = kind; ShortName = shortName; Folder = folder; _baseUrl = baseUrl; _files = files; }

    public static LanguagePack Light { get; } = new(PackKind.Light, "الخفيفة", "opus-mt-en-ar",
        "https://huggingface.co/Xenova/opus-mt-en-ar/resolve/63765349ac9f6c43f6e5bd8c0a5583224cfd8587/",
        new[]
        {
            new ModelFile("config.json", 1_400), new ModelFile("generation_config.json", 300),
            new ModelFile("source.spm", 801_000), new ModelFile("target.spm", 917_000),
            new ModelFile("vocab.json", 2_240_000),
            new ModelFile("onnx/encoder_model_quantized.onnx", 51_800_000),
            new ModelFile("onnx/decoder_model_quantized.onnx", 91_200_000),
        });

    public static LanguagePack Heavy { get; } = new(PackKind.Heavy, "التقيلة", "nllb-200-600m",
        "https://huggingface.co/Xenova/nllb-200-distilled-600M/resolve/bfaad393bfa9f83f73fb0ddaeb21f42d3323e821/",
        new[]
        {
            new ModelFile("config.json", 1_500), new ModelFile("generation_config.json", 300),
            new ModelFile("tokenizer.json", 17_300_000),
            new ModelFile("onnx/encoder_model_quantized.onnx", 419_000_000),
            new ModelFile("onnx/decoder_model_merged_quantized.onnx", 476_000_000),
        });

    // Pinned Apache-2.0 MADLAD-400-3B INT4 ONNX export. External weight sidecars are required by ONNX Runtime.
    public static LanguagePack Madlad { get; } = new(PackKind.Madlad, "MADLAD-400 3B",
        "madlad-400-3b-int4",
        "https://huggingface.co/tonythethompson/madlad400-3b-mt-onnx/resolve/c165d741425c82365761aae98d0891bd9e7995ed/mixed_fp16_int4/",
        new[]
        {
            new ModelFile("config.json", 850),
            new ModelFile("generation_config.json", 184),
            new ModelFile("spiece.model", 4_427_844,
                "ef11ac9a22c7503492f56d48dce53be20e339b63605983e9f27d2cd0e0f3922c", true),
            new ModelFile("onnx/encoder_model.onnx", 1_473_277,
                "2be68c85bb5c9468e389aed3d4d7563b503f05918f244c560bf3729709380bb2", true),
            new ModelFile("onnx/encoder_model.onnx.data", 1_610_746_877,
                "612c552d68ff5e702f95bcc56996da5069a4c5e50895599e797f7bb27cd0c422", true),
            new ModelFile("onnx/decoder_model_merged.onnx", 3_228_557,
                "0b2dcd996563fea546576cb38e2195da5f0069f01fd08c77c6b6b9408ea1dbc5", true),
            new ModelFile("onnx/decoder_model_merged.onnx.data", 2_051_607_552,
                "62d253bc68aca845129dbba3c04a6e21d2fcf080ab4cb8cad982622a8374e3f9", true),
        });

    public string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAFYO", "models", Folder);

    public long ApproxBytes => _files.Sum(f => f.Bytes);

    public bool IsInstalled()
    {
        if (!File.Exists(Path.Combine(Dir, Marker))) return false;
        return _files.All(f =>
        {
            var info = new FileInfo(Path.Combine(Dir, f.File));
            return info.Exists && info.Length > 0 && (!f.ExactLength || info.Length == f.Bytes);
        });
    }

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Dir;
        Directory.CreateDirectory(dir);
        long total = ApproxBytes, done = 0;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        foreach (var file in _files)
        {
            var target = Path.Combine(dir, file.File);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            bool validExisting = File.Exists(target) && new FileInfo(target).Length > 0 &&
                (!file.ExactLength || new FileInfo(target).Length == file.Bytes);
            if (validExisting && file.Sha256 != null)
            {
                await using var existing = File.OpenRead(target);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false)).ToLowerInvariant();
                validExisting = hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!validExisting) File.Delete(target);
            }
            if (validExisting)
            {
                done += file.Bytes;
                progress?.Report((double)done / total);
                continue;
            }

            var part = target + ".part";
            try
            {
                using (var response = await http.GetAsync(_baseUrl + file.File,
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var destination = File.Create(part);
                    var buffer = new byte[1024 * 1024];
                    int read;
                    long downloaded = 0;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        downloaded += read;
                        progress?.Report(Math.Min(1.0, (double)(done + Math.Min(downloaded, file.Bytes)) / total));
                    }
                }

                var actualLength = new FileInfo(part).Length;
                if (file.ExactLength && actualLength != file.Bytes)
                    throw new InvalidDataException($"حجم الملف {file.File} غير مطابق للنسخة المعتمدة.");
                if (file.Sha256 != null)
                {
                    await using var downloadedFile = File.OpenRead(part);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(downloadedFile, ct).ConfigureAwait(false)).ToLowerInvariant();
                    if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"فشل التحقق من سلامة الملف {file.File} (SHA-256).");
                }
                File.Move(part, target, overwrite: true);
                done += file.Bytes;
            }
            catch
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                throw;
            }
        }
        File.WriteAllText(Path.Combine(dir, Marker), DateTime.UtcNow.ToString("O"));
        progress?.Report(1.0);
    }

    /// <summary>Loads the selected pack; do this through LazyTranslator so model loading stays off the UI thread.</summary>
    public ITranslator CreateTranslator(int threads, int beams)
    {
        return Kind switch
        {
            PackKind.Madlad => OnnxMadladTranslator.Load(Dir, Math.Min(threads, 1)),
            PackKind.Heavy => OnnxNllbTranslator.Load(Dir, threads),
            _ => CreateLightTranslator(threads, beams),
        };
    }

    private ITranslator CreateLightTranslator(int threads, int beams)
    {
        var translator = OnnxMarianTranslator.Load(Dir, threads);
        translator.Beams = beams;
        return translator;
    }
}
