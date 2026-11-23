using System.IO.Compression;
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

    // Pinned Apache-2.0 MADLAD-400 ReducedRam INT4 export. Each extracted graph is SHA-256 verified.
    public static LanguagePack Madlad { get; } = new(PackKind.Madlad, "MADLAD-400 3B",
        "madlad-400-3b-int4", "",
        new[]
        {
            new ModelFile("Madlad/madlad_embed_8bit.onnx", 262_144_789,
                "350bc365a3f4f3d9d48762e079526fcf9efe7a4ab7a789178dcda1a8996aeb65", true),
            new ModelFile("Madlad/Int4Acc4/madlad_encoder_4bit.onnx", 575_492_499,
                "0ec36a3020415b6e07b731d4d5d259850b1e5247436a21cb77be8af55dc4eac1", true),
            new ModelFile("Madlad/Int4Acc4/madlad_cache_initializer_4bit.onnx", 71_888_403,
                "0c053db1692e263e1d4e5e74389164623c6bc6548555151fa258876979b2c5cf", true),
            new ModelFile("Madlad/Int4Acc4/madlad_decoder_4bit.onnx", 788_164_973,
                "fe76451876d3fa167879dd4e643172697bea7bbf72baa264528219401240fd8a", true),
            new ModelFile("Madlad/spiece.model", 4_427_844,
                "ef11ac9a22c7503492f56d48dce53be20e339b63605983e9f27d2cd0e0f3922c", true),
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
        if (Kind == PackKind.Madlad)
        {
            await DownloadMadladArchiveAsync(progress, ct).ConfigureAwait(false);
            return;
        }

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
                string hash;
                await using (var existing = File.OpenRead(target))
                {
                    hash = Convert.ToHexString(
                        await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false)).ToLowerInvariant();
                }
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

    private const long MadladArchiveBytes = 1_482_639_037;
    private const string MadladArchiveUrl =
        "https://github.com/niedev/OnnxModelsEnhancer/releases/download/v1.0.0-beta/Madlad.zip";

    private async Task DownloadMadladArchiveAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Dir;
        Directory.CreateDirectory(dir);
        if (IsInstalled())
        {
            progress?.Report(1.0);
            return;
        }

        var archivePath = Path.Combine(dir, "Madlad.zip");
        var partialArchivePath = archivePath + ".part";
        try
        {
            using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            using (var response = await http.GetAsync(MadladArchiveUrl,
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var destination = File.Create(partialArchivePath);
                var buffer = new byte[1024 * 1024];
                long downloaded = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    downloaded += read;
                    progress?.Report(0.65 * Math.Min(1.0, (double)downloaded / MadladArchiveBytes));
                }
            }

            if (new FileInfo(partialArchivePath).Length != MadladArchiveBytes)
                throw new InvalidDataException("حجم أرشيف MADLAD لا يطابق النسخة المعتمدة.");

            File.Move(partialArchivePath, archivePath, overwrite: true);
            using var archive = ZipFile.OpenRead(archivePath);
            long extractedBytes = 0;
            foreach (var file in _files)
            {
                ct.ThrowIfCancellationRequested();
                var entry = archive.GetEntry(file.File)
                    ?? throw new InvalidDataException("ملف MADLAD غير موجود داخل الأرشيف: " + file.File);
                if (entry.Length != file.Bytes)
                    throw new InvalidDataException("حجم ملف MADLAD غير مطابق: " + file.File);

                var target = Path.Combine(dir, file.File);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var validExisting = File.Exists(target) && new FileInfo(target).Length == file.Bytes;
                if (validExisting)
                {
                    string existingHash;
                    await using (var existing = File.OpenRead(target))
                    {
                        existingHash = Convert.ToHexString(
                            await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false)).ToLowerInvariant();
                    }
                    validExisting = existingHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
                }

                if (!validExisting)
                {
                    var part = target + ".part";
                    try
                    {
                        await using (var input = entry.Open())
                        await using (var output = File.Create(part))
                            await input.CopyToAsync(output, 1024 * 1024, ct).ConfigureAwait(false);

                        var actualLength = new FileInfo(part).Length;
                        if (actualLength != file.Bytes)
                            throw new InvalidDataException("حجم ملف MADLAD بعد فك الضغط غير مطابق: " + file.File);

                        string hash;
                        await using (var extracted = File.OpenRead(part))
                        {
                            hash = Convert.ToHexString(
                                await SHA256.HashDataAsync(extracted, ct).ConfigureAwait(false)).ToLowerInvariant();
                        }
                        if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("فشل التحقق من SHA-256 لملف MADLAD: " + file.File);

                        File.Move(part, target, overwrite: true);
                    }
                    catch
                    {
                        try { if (File.Exists(part)) File.Delete(part); } catch { }
                        throw;
                    }
                }

                extractedBytes += file.Bytes;
                progress?.Report(0.65 + 0.35 * ((double)extractedBytes / ApproxBytes));
            }

            File.WriteAllText(Path.Combine(dir, Marker), DateTime.UtcNow.ToString("O"));
            progress?.Report(1.0);
        }
        finally
        {
            try { if (File.Exists(partialArchivePath)) File.Delete(partialArchivePath); } catch { }
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
        }
    }

    /// <summary>Loads the selected pack; do this through LazyTranslator so model loading stays off the UI thread.</summary>
    public ITranslator CreateTranslator(int threads, int beams)
    {
        return Kind switch
        {
            // This pack uses the verified four-graph ReducedRam runtime, not the merged-decoder path.
            PackKind.Madlad => OnnxMadladTranslator.Load(Dir, threads),
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
