using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flafyo.Core.GameText;

public sealed record GameFileInfo(string FullPath, string RelativePath, string Format, int StringCount, long Bytes);

public sealed class GameScanOptions
{
    public bool IncludePlainText { get; init; }
    public long MaxFileBytes { get; init; } = 30L * 1024 * 1024;
}

public sealed class GameTranslateOptions
{
    /// <summary>Join and reverse the Arabic letters, for engines that cannot display Arabic by themselves.</summary>
    public bool ShapeArabic { get; init; }
    public int SaveEvery { get; init; } = 100;
    /// <summary>0 = everything. A small number gives a quick test run (the first strings of every file).</summary>
    public int MaxStringsPerFile { get; init; }
    public Action<string>? Log { get; init; }
}

public sealed record GameProgress(int FileIndex, int FileCount, string File, int Done, int Total, int Errors);

public sealed class GameTranslationReport
{
    public int Files, Strings, FromMemory, Errors, SkippedFiles;
    public TimeSpan Elapsed;
}

/// <summary>Finds the text files of a game folder that contain translatable strings.</summary>
public static class GameScanner
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "FLAFYO_backup", ".git", "node_modules", "__MACOSX" };

    public static List<GameFileInfo> Scan(string root, GameScanOptions? options, CancellationToken ct, Action<string>? log = null)
    {
        var o = options ?? new GameScanOptions();
        var result = new List<GameFileInfo>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    if (!SkipDirs.Contains(Path.GetFileName(sub))) stack.Push(sub);

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var format = GameFormats.For(file, o.IncludePlainText);
                    if (format == null) continue;
                    var info = new FileInfo(file);
                    if (info.Length == 0 || info.Length > o.MaxFileBytes) continue;
                    try
                    {
                        var text = TextFileIO.Read(file).Text;
                        int count = 0;
                        format.Transform(text, _ => { count++; return null; });
                        if (count > 0) result.Add(new GameFileInfo(file, Path.GetRelativePath(root, file), format.Name, count, info.Length));
                    }
                    catch (Exception ex) { log?.Invoke($"تخطي {Path.GetFileName(file)}: {ex.Message}"); }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return result.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>
/// Translates the strings of game files with the AI translator and writes translated copies (the originals are never touched).
/// Translations are remembered in the output folder, so an interrupted run continues where it stopped.
/// </summary>
public sealed class GameTextTranslator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Regex XmlEncoding = new("encoding=\"[^\"]*\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TranslationResolver _resolver;
    private readonly Dictionary<string, string> _memory = new(StringComparer.Ordinal);
    private string _memoryPath = "";
    private int _unsaved;

    public GameTextTranslator(TranslationResolver resolver) => _resolver = resolver;

    public int MemorySize => _memory.Count;

    public Task<GameTranslationReport> TranslateAsync(IReadOnlyList<GameFileInfo> files, string outputRoot,
        GameTranslateOptions? options, IProgress<GameProgress>? progress, CancellationToken ct) =>
        Task.Run(() => Run(files, outputRoot, options ?? new GameTranslateOptions(), progress, ct), ct);

    private GameTranslationReport Run(IReadOnlyList<GameFileInfo> files, string outputRoot, GameTranslateOptions opt,
        IProgress<GameProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var report = new GameTranslationReport();
        Directory.CreateDirectory(outputRoot);
        _memoryPath = Path.Combine(outputRoot, "_flafyo_memory.json");
        LoadMemory();
        try
        {
            for (int index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var f = files[index];
                var format = GameFormats.For(f.FullPath, true);
                if (format == null) continue;

                var decoded = TextFileIO.Read(f.FullPath);
                int done = 0, fileIndex = index + 1;
                var newText = format.Transform(decoded.Text, source =>
                {
                    ct.ThrowIfCancellationRequested();
                    done++;
                    if (opt.MaxStringsPerFile > 0 && done > opt.MaxStringsPerFile) return null;
                    var r = TranslateOne(source, opt, report, ct);
                    progress?.Report(new GameProgress(fileIndex, files.Count, f.RelativePath, done, f.StringCount, report.Errors));
                    return r;
                });

                var ext = Path.GetExtension(f.FullPath).ToLowerInvariant();
                if (OutputValidator.Parses(ext, decoded.Text) && !OutputValidator.Parses(ext, newText))
                {
                    report.SkippedFiles++;                      // never hand the game a file that no longer parses
                    opt.Log?.Invoke($"تخطيت {f.RelativePath}: الناتج مش صالح (الملف الأصلي اتحفظ زي ما هو).");
                    continue;
                }
                var outPath = SafeOutputPath(outputRoot, f.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                if (outPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && decoded.Encoding is not System.Text.UTF8Encoding)
                    newText = XmlEncoding.Replace(newText, "encoding=\"utf-8\"", 1);
                File.WriteAllBytes(outPath, TextFileIO.Encode(newText, decoded));
                report.Files++;
            }
        }
        finally
        {
            SaveMemory();
            report.Elapsed = sw.Elapsed;
        }
        return report;
    }

    private string? TranslateOne(string source, GameTranslateOptions opt, GameTranslationReport report, CancellationToken ct)
    {
        if (!_memory.TryGetValue(source, out var translated))
        {
            try
            {
                translated = PlaceholderGuard.TranslateProtected(source,
                    part => _resolver.ResolveAsync(part, ct).GetAwaiter().GetResult().Text);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { report.Errors++; return null; }

            if (string.IsNullOrWhiteSpace(translated)) { report.Errors++; return null; }
            _memory[source] = translated;
            if (++_unsaved >= opt.SaveEvery) SaveMemory();
        }
        else report.FromMemory++;

        report.Strings++;
        if (translated == source) return null;
        return opt.ShapeArabic ? ArabicShaper.ToVisualProtected(translated) : translated;
    }

    private static string SafeOutputPath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe path: " + relative);
        return full;
    }

    private void LoadMemory()
    {
        _memory.Clear();
        try
        {
            if (File.Exists(_memoryPath))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_memoryPath));
                if (d != null) foreach (var kv in d) _memory[kv.Key] = kv.Value;
            }
        }
        catch { /* a damaged memory file only means translating again */ }
    }

    private void SaveMemory()
    {
        try { File.WriteAllText(_memoryPath, JsonSerializer.Serialize(_memory, JsonOptions)); _unsaved = 0; }
        catch { }
    }
}

/// <summary>Copies translated files into the game folder after backing up the originals, and restores them on request.</summary>
public static class GameInstaller
{
    public static string BackupRoot(string gameRoot) => Path.Combine(gameRoot, "FLAFYO_backup");

    private static void Validate(string gameRoot)
    {
        if (!Directory.Exists(gameRoot)) throw new InvalidOperationException("مجلد اللعبة مش موجود.");
        var full = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(Path.GetFullPath(gameRoot))?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
        if (full.Length == 0 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("اختار مجلد اللعبة نفسه مش درايف كامل.");
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (win.Length > 0 && full.StartsWith(win, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("مجلدات Windows مش مسموحة.");
    }

    private static string SafeCombine(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe path: " + relative);
        return full;
    }

    /// <summary>Replaces existing game files with their translated versions. Returns the number of files replaced.</summary>
    public static int Apply(string translatedRoot, string gameRoot)
    {
        Validate(gameRoot);
        var backup = BackupRoot(gameRoot);
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(translatedRoot, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith("_flafyo", StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetRelativePath(translatedRoot, file);
            var target = SafeCombine(gameRoot, rel);
            if (!File.Exists(target)) continue;                         // only files that already exist in the game are replaced

            var saved = SafeCombine(backup, rel);
            if (!File.Exists(saved))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(target, saved);                               // the first backup is the real original and is never overwritten
            }
            File.SetAttributes(target, FileAttributes.Normal);
            File.Copy(file, target, overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>Puts the original files back. Returns the number of files restored.</summary>
    public static int Restore(string gameRoot)
    {
        Validate(gameRoot);
        var backup = BackupRoot(gameRoot);
        if (!Directory.Exists(backup)) return 0;
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(backup, file);
            var target = SafeCombine(gameRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
            File.Copy(file, target, overwrite: true);
            count++;
        }
        return count;
    }
}

/// <summary>Checks that a translated file is still valid for the game.</summary>
public static class OutputValidator
{
    public static bool Parses(string extension, string text)
    {
        try
        {
            switch (extension)
            {
                case ".json":
                    using (JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })) { }
                    return true;
                case ".xml":
                case ".resx":
                    System.Xml.Linq.XDocument.Parse(text);
                    return true;
                default:
                    return true;
            }
        }
        catch { return false; }
    }
}

/// <summary>Helps pick the files that really hold the game's text, and shows a few strings so the user can judge.</summary>
public static class GameFileHeuristics
{
    private static readonly HashSet<string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        "ui", "en", "eng", "english", "enus", "l10n", "i18n", "text", "texts", "lang", "langs", "language", "languages", "strings",
        "string", "dialog", "dialogs", "dialogue", "dialogues", "subtitle", "subtitles", "message", "messages", "quest", "quests",
        "menu", "menus", "caption", "captions", "locale", "locales", "localization", "localisation", "translation", "translations",
    };

    public static bool LooksLikeLocalization(string relativePath)
    {
        var tokens = Regex.Split(relativePath.ToLowerInvariant(), @"[^a-z0-9]+").Where(t => t.Length > 0);
        return tokens.Any(t => Hints.Contains(t) || t.StartsWith("locali", StringComparison.Ordinal) || t.StartsWith("transl", StringComparison.Ordinal));
    }

    public static List<string> Sample(string path, int max)
    {
        var list = new List<string>();
        var format = GameFormats.For(path, true);
        if (format == null) return list;
        format.Transform(TextFileIO.Read(path).Text, s => { if (list.Count < max) list.Add(s); return null; });
        return list;
    }
}
