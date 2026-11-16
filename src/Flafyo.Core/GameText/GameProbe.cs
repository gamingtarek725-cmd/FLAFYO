using System.IO.Compression;
using System.Text;

namespace Flafyo.Core.GameText;

public sealed record FoundText(string Text, long Offset, bool Utf16);

/// <summary>Finds readable English text inside any binary data (like the "strings" tool, but only keeps real sentences).</summary>
public static class BinaryTextMiner
{
    private static bool Printable(byte b) => (b >= 0x20 && b < 0x7F) || b == 9 || b == 10 || b == 13;

    public static List<FoundText> Extract(ReadOnlySpan<byte> d, int minChars = 14, int maxResults = 200_000)
    {
        var results = new List<FoundText>();
        int n = d.Length;

        // plain ASCII / UTF-8 text
        for (int i = 0; i < n && results.Count < maxResults;)
        {
            if (!Printable(d[i])) { i++; continue; }
            int start = i;
            while (i < n && Printable(d[i])) i++;
            if (i - start >= minChars) Add(results, Encoding.ASCII.GetString(d.Slice(start, i - start)), start, false, minChars);
        }

        // UTF-16 little-endian text (every second byte is zero)
        for (int s = 0; s < n - 1 && results.Count < maxResults;)
        {
            if (Printable(d[s]) && d[s + 1] == 0)
            {
                int e = s;
                while (e + 1 < n && Printable(d[e]) && d[e + 1] == 0) e += 2;
                if ((e - s) / 2 >= minChars) Add(results, Encoding.Unicode.GetString(d.Slice(s, e - s)), s, true, minChars);
                s = e;
            }
            else s++;
        }
        return results;
    }

    private static void Add(List<FoundText> results, string raw, long offset, bool utf16, int minChars)
    {
        var t = TextNormalizer.Normalize(raw);
        if (t.Length < minChars || !t.Contains(' ') || !TextQuality.LooksLikeText(t)) return;
        results.Add(new FoundText(t, offset, utf16));
    }

    /// <summary>A "sentence" has at least four words (binary garbage rarely lines up three spaces by chance).</summary>
    public static bool IsSentence(string s) => s.Count(c => c == ' ') >= 3;

    // Real English sentences almost always contain one of these; random bytes that happen to look printable almost never do.
    // This is the main thing that keeps binary game resources (models, audio, compiled code) from being mistaken for dialogue.
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","you","your","to","of","is","in","a","an","that","it","this","with","for","on","we","as","at","by",
        "but","not","are","was","were","have","has","had","will","would","can","could","from","my","me","he","she",
        "they","them","his","her","our","us","be","been","do","does","did","what","when","where","how","why","who",
        "if","then","than","so","out","up","down","now","all","no","yes","get","got","just","there","here","about",
    };

    public static bool HasCommonWord(string s)
    {
        foreach (var raw in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = raw.Trim('.', ',', '!', '?', ':', ';', '"', ''', '(', ')');
            if (CommonWords.Contains(w)) return true;
        }
        return false;
    }

    /// <summary>The stricter check actually used to decide whether a found string is real dialogue/text, not binary noise.</summary>
    public static bool IsRealSentence(string s) => IsSentence(s) && HasCommonWord(s);

    /// <summary>Fraction of bytes that look like normal text (a cheap way to skip binary files before the expensive scan).</summary>
    public static double PrintableRatio(byte[] data, int maxSamples = 200_000)
    {
        if (data.Length == 0) return 0;
        int stride = Math.Max(1, data.Length / maxSamples);
        int printable = 0, n = 0;
        for (int i = 0; i < data.Length; i += stride)
        {
            byte b = data[i];
            if ((b >= 0x20 && b < 0x7F) || b == 9 || b == 10 || b == 13) printable++;
            n++;
        }
        return n == 0 ? 0 : (double)printable / n;
    }
}

public sealed record ProbeHit(string FullPath, string? Entry, string Kind, int Sentences, int Strings,
    IReadOnlyList<string> Samples, IReadOnlyList<FoundText> All)
{
    public string Title => Entry == null ? FullPath : $"{FullPath}  ›  {Entry}";
}

/// <summary>Looks through a whole game folder (and inside ARC/ZIP archives) for the files that really hold the game's text.</summary>
public static class GameProbe
{
    private static readonly HashSet<string> SkipExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".dds", ".tex", ".ico", ".ogg", ".wav", ".mp3", ".mp4", ".bik", ".wmv", ".avi", ".webm",
        ".ttf", ".otf", ".exe", ".dll", ".pdb", ".lib", ".obj", ".sys", ".cab", ".msi", ".bk2", ".usm", ".wem", ".fsb", ".bank",
    };

    public static List<ProbeHit> Scan(string root, CancellationToken ct, Action<string>? progress = null,
        long maxFileBytes = 300L * 1024 * 1024, int minSentences = 5)
    {
        var hits = new List<ProbeHit>();
        var stack = new Stack<string>();
        stack.Push(root);
        int seen = 0;
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    if (!Path.GetFileName(sub).Equals("FLAFYO_backup", StringComparison.OrdinalIgnoreCase)) stack.Push(sub);
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    if (++seen % 20 == 0) progress?.Invoke($"فحصت {seen} ملف… (لقيت {hits.Count} ملف فيه نصوص)");
                    try { ScanFile(file, hits, maxFileBytes, minSentences, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch { /* an unreadable file is simply skipped */ }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        progress?.Invoke($"خلص الفحص: {seen} ملف.");
        return hits.OrderByDescending(h => h.Sentences).Take(300).ToList();
    }

    private static void ScanFile(string file, List<ProbeHit> hits, long maxFileBytes, int minSentences, CancellationToken ct)
    {
        var info = new FileInfo(file);
        if (info.Length == 0 || info.Length > maxFileBytes) return;
        var ext = info.Extension;
        if (SkipExt.Contains(ext)) return;

        var data = File.ReadAllBytes(file);
        if (ArcArchive.IsArc(data))
        {
            var arc = ArcArchive.Parse(data);
            foreach (var e in arc.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (e.IsTexture || e.Size > 80_000_000) continue;          // pictures never hold text
                try { Add(hits, file, e.Name + " (" + e.TypeName + ")", "ARC", arc.Read(e), minSentences); } catch { }
            }
            return;
        }
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            foreach (var e in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (e.Length == 0 || e.Length > 50_000_000 || SkipExt.Contains(Path.GetExtension(e.FullName))) continue;
                using var s = e.Open(); using var ms = new MemoryStream();
                s.CopyTo(ms);
                Add(hits, file, e.FullName, "ZIP", ms.ToArray(), minSentences);
            }
            return;
        }
        Add(hits, file, null, "ملف", data, minSentences);
    }

    /// <summary>Below this, a file is almost certainly binary data, not text — skipped before the (slow, noise-prone) string scan.</summary>
    private const double MinPrintableRatio = 0.55;

    private static void Add(List<ProbeHit> hits, string file, string? entry, string kind, byte[] data, int minSentences)
    {
        if (BinaryTextMiner.PrintableRatio(data) < MinPrintableRatio) return;    // binary resource (model, audio, texture, compiled code...)
        var found = BinaryTextMiner.Extract(data);
        var sentences = found.Where(f => BinaryTextMiner.IsRealSentence(f.Text)).ToList();
        if (sentences.Count < minSentences) return;
        hits.Add(new ProbeHit(file, entry, kind, sentences.Count, found.Count,
            sentences.Take(8).Select(s => s.Text).ToList(), found));
    }

    /// <summary>Writes every text found in a hit to a UTF-8 file ("offset  encoding  text").</summary>
    public static string Export(ProbeHit hit, string folder)
    {
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(hit.FullPath) + (hit.Entry == null ? "" : "_" + Path.GetFileName(hit.Entry.Replace('\\', '/').Split(' ')[0]));
        name = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        var path = Path.Combine(folder, name + ".txt");
        File.WriteAllLines(path, hit.All.Select(f => $"{f.Offset:X8}\t{(f.Utf16 ? "utf16" : "ascii")}\t{f.Text}"), new UTF8Encoding(false));
        return path;
    }

    /// <summary>Unpacks every entry of an ARC archive into a folder (for use with other modding tools).</summary>
    public static int ExtractArc(string arcPath, string folder)
    {
        var arc = ArcArchive.Parse(File.ReadAllBytes(arcPath));
        Directory.CreateDirectory(folder);
        var index = new List<string>();
        for (int i = 0; i < arc.Entries.Count; i++)
        {
            var e = arc.Entries[i];
            var safe = new string(e.Name.Replace('\\', '_').Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            var file = $"{i:D3}_{safe}.{e.TypeHash:X8}.bin";
            File.WriteAllBytes(Path.Combine(folder, file), arc.Read(e));
            index.Add($"{file}\t{e.Name}\ttype={e.TypeName}\tflags=0x{e.Flags:X8}");
        }
        File.WriteAllLines(Path.Combine(folder, "_index.txt"), index);
        return arc.Entries.Count;
    }
}
