using System.IO.Compression;

namespace Flafyo.Core.GameText;

public enum ContainerKind { None, Arc, Zip }

/// <summary>
/// Detects, extracts, and rebuilds the archive containers found in game folders: Capcom-style ARC (verified on a real
/// Resident Evil 6 file) and ZIP (including files that are ZIP but use a different extension, like Dying Light's
/// ".pak" — confirmed by the game's own modding community, since Windows' built-in zip tools open them directly).
/// </summary>
public static class ContainerTools
{
    private const string IndexFile = "_flafyo_container.txt";
    private const long MaxContainerBytes = 512L * 1024 * 1024;
    private const long MaxExtractedBytes = 512L * 1024 * 1024;
    private const long MaxEntryBytes = 128L * 1024 * 1024;
    private const int MaxEntries = 10000;

    public static ContainerKind Detect(byte[] data)
    {
        if (ArcArchive.IsArc(data)) return ContainerKind.Arc;
        if (data.Length >= 4 && data[0] == 'P' && data[1] == 'K' && (data[2] == 3 || data[2] == 5 || data[2] == 7)) return ContainerKind.Zip;
        return ContainerKind.None;
    }

    /// <summary>Unpacks every entry into a folder, plus an index file that Repack needs to rebuild the same container later.</summary>
    public static (ContainerKind Kind, int Count) Extract(string containerPath, string outputFolder)
    {
        var info = new FileInfo(containerPath);
        if (!info.Exists) throw new FileNotFoundException("ملف الحاوية غير موجود.", containerPath);
        if (info.Length > MaxContainerBytes) throw new InvalidDataException("حجم الحاوية أكبر من حد الاستخراج الآمن (512 MiB).");
        var data = File.ReadAllBytes(containerPath);
        var kind = Detect(data);
        Directory.CreateDirectory(outputFolder);
        var index = new List<string> { "FLAFYO_CONTAINER_V1", kind.ToString() };

        if (kind == ContainerKind.Arc)
        {
            var arc = ArcArchive.Parse(data);
            if (arc.Entries.Count > MaxEntries) throw new InvalidDataException("أرشيف ARC يحتوي على أكثر من 10,000 عنصر.");
            long total = 0;
            for (int i = 0; i < arc.Entries.Count; i++)
            {
                var e = arc.Entries[i];
                if (e.Size > MaxEntryBytes || (total += e.Size) > MaxExtractedBytes)
                    throw new InvalidDataException("حجم عناصر ARC يتجاوز حدود الاستخراج الآمن.");
                var rel = SafeRelativePath(e.Name, i, "." + e.TypeHash.ToString("X8") + ".bin");
                var dest = Path.Combine(outputFolder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, arc.Read(e));
                index.Add($"{rel}\t{Convert.ToBase64String(e.RawName)}\t{e.TypeHash:X8}\t{e.Flags:X8}");
            }
            File.WriteAllLines(Path.Combine(outputFolder, IndexFile), index);
            return (kind, arc.Entries.Count);
        }
        if (kind == ContainerKind.Zip)
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            int n = 0;
            long total = 0;
            foreach (var e in zip.Entries)
            {
                if (++n > MaxEntries) throw new InvalidDataException("الأرشيف يحتوي على أكثر من 10,000 عنصر.");
                if (e.FullName.EndsWith('/') || e.Length == 0 && e.Name.Length == 0) { n--; continue; }
                if (e.Length < 0 || e.Length > MaxEntryBytes || (total += e.Length) > MaxExtractedBytes)
                    throw new InvalidDataException("الحجم غير المضغوط للأرشيف يتجاوز حدود الاستخراج الآمن.");
                var rel = NormalizeArchivePath(e.FullName);
                var dest = ResolveSafePath(outputFolder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var input = e.Open();
                using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                CopyBounded(input, output, MaxEntryBytes);
                index.Add(rel.Replace(Path.DirectorySeparatorChar, '/'));
            }
            File.WriteAllLines(Path.Combine(outputFolder, IndexFile), index);
            return (kind, n);
        }
        throw new InvalidDataException("الملف ده مش أرشيف ARC أو ZIP معروف.");
    }

    /// <summary>Rebuilds a container from a folder that Extract produced (its files may have been edited/translated since).</summary>
    public static byte[] Repack(string extractedFolder)
    {
        var indexPath = Path.Combine(extractedFolder, IndexFile);
        if (!File.Exists(indexPath)) throw new InvalidOperationException("المجلد ده مش ناتج استخراج حاوية (مفيش فهرس).");
        var lines = File.ReadAllLines(indexPath);
        if (lines.Length < 2 || lines[0] != "FLAFYO_CONTAINER_V1") throw new InvalidOperationException("فهرس الحاوية غير مفهوم.");
        var kind = Enum.Parse<ContainerKind>(lines[1]);

        if (kind == ContainerKind.Arc)
        {
            var files = new List<(ArcEntry Template, byte[] Data)>();
            long total = 0;
            foreach (var line in lines.Skip(2))
            {
                var p = line.Split('\t');
                if (p.Length != 4 || files.Count >= MaxEntries) throw new InvalidDataException("فهرس ARC غير صالح أو يحتوي عناصر أكثر من الحد.");
                var raw = Convert.FromBase64String(p[1]);
                int len = Array.IndexOf(raw, (byte)0); if (len < 0) len = raw.Length;
                var rel = NormalizeArchivePath(p[0]);
                var source = ResolveSafePath(extractedFolder, rel);
                if (!File.Exists(source)) throw new FileNotFoundException("ملف عنصر ARC غير موجود.", source);
                var size = new FileInfo(source).Length;
                if (size > MaxEntryBytes || (total += size) > MaxExtractedBytes)
                    throw new InvalidDataException("حجم ملفات ARC يتجاوز الحدود الآمنة.");
                var template = new ArcEntry
                {
                    RawName = raw, Name = System.Text.Encoding.Latin1.GetString(raw, 0, len),
                    TypeHash = Convert.ToUInt32(p[2], 16), SizeAndFlags = Convert.ToUInt32(p[3], 16),
                };
                files.Add((template, File.ReadAllBytes(source)));
            }
            return ArcArchive.Build(7, files);
        }
        if (kind == ContainerKind.Zip)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                int count = 0;
                long total = 0;
                foreach (var rawRel in lines.Skip(2))
                {
                    if (++count > MaxEntries) throw new InvalidDataException("الفهرس يحتوي على أكثر من 10,000 عنصر.");
                    var rel = NormalizeArchivePath(rawRel);
                    var source = ResolveSafePath(extractedFolder, rel);
                    if (!File.Exists(source)) throw new FileNotFoundException("ملف ZIP غير موجود.", source);
                    var length = new FileInfo(source).Length;
                    if (length > MaxEntryBytes || (total += length) > MaxExtractedBytes)
                        throw new InvalidDataException("حجم ملفات ZIP يتجاوز الحدود الآمنة.");
                    var entry = zip.CreateEntry(rel.Replace(Path.DirectorySeparatorChar, '/'), CompressionLevel.Optimal);
                    using var output = entry.Open();
                    using var input = File.OpenRead(source);
                    CopyBounded(input, output, MaxEntryBytes);
                }
            }
            return ms.ToArray();
        }
        throw new InvalidOperationException("نوع الحاوية غير مدعوم: " + kind);
    }

    /// <summary>Writes the rebuilt container over the original, after backing the original up next to it (once).</summary>
    public static string Apply(string originalContainerPath, byte[] rebuilt)
    {
        ArgumentNullException.ThrowIfNull(rebuilt);
        var originalInfo = new FileInfo(originalContainerPath);
        if (!originalInfo.Exists) throw new FileNotFoundException("ملف الحاوية الأصلي غير موجود.", originalContainerPath);
        if (originalInfo.Length <= 0 || originalInfo.Length > MaxContainerBytes || rebuilt.Length <= 0 || rebuilt.LongLength > MaxContainerBytes)
            throw new InvalidDataException("حجم الحاوية الأصلية أو الناتجة خارج الحدود الآمنة.");

        var originalKind = Detect(File.ReadAllBytes(originalContainerPath));
        var rebuiltKind = Detect(rebuilt);
        if (originalKind == ContainerKind.None || rebuiltKind != originalKind)
            throw new InvalidDataException("نوع الحاوية الناتجة لا يطابق نوع الملف الأصلي.");

        var backup = originalContainerPath + ".flafyo_backup";
        var temp = originalContainerPath + ".flafyo_tmp";
        var verifyArchive = temp + ".verify";
        var verifyFolder = temp + ".verify_dir";
        try
        {
            // Validate the exact bytes that will be applied by reopening them through the bounded extractor.
            File.WriteAllBytes(verifyArchive, rebuilt);
            var (_, verifiedCount) = Extract(verifyArchive, verifyFolder);
            if (verifiedCount == 0)
                throw new InvalidDataException("الحاوية الناتجة لا تحتوي على عناصر قابلة للتحقق.");

            if (!File.Exists(backup)) File.Copy(originalContainerPath, backup);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                stream.Write(rebuilt, 0, rebuilt.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, originalContainerPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (File.Exists(verifyArchive)) File.Delete(verifyArchive);
            if (Directory.Exists(verifyFolder)) Directory.Delete(verifyFolder, recursive: true);
        }
        return backup;
    }

    public static bool Restore(string originalContainerPath)
    {
        var backup = originalContainerPath + ".flafyo_backup";
        if (!File.Exists(backup)) return false;
        File.Copy(backup, originalContainerPath, overwrite: true);
        return true;
    }

    private static string NormalizeArchivePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("مسار عنصر الأرشيف فارغ.");
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(part => part == ".." || part == "." || part.Contains(':')))
            throw new InvalidDataException("الأرشيف يحتوي على مسار غير آمن: " + relativePath);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new InvalidDataException("مسار عنصر الأرشيف غير صالح.");
        return Path.Combine(segments);
    }

    private static string ResolveSafePath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("مسار الملف يخرج من مجلد الاستخراج.");
        return fullPath;
    }

    private static void CopyBounded(Stream input, Stream output, long limit)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += read;
            if (copied > limit) throw new InvalidDataException("تجاوز عنصر الأرشيف حد الحجم أثناء الاستخراج.");
            output.Write(buffer, 0, read);
        }
    }

    /// <summary>Keeps the entry's own folders (so files that belong together stay together) and only makes the FILE NAME
    /// unique by numbering it — numbering the whole path instead would split one shared folder into several numbered ones.</summary>
    private static string SafeRelativePath(string entryName, int index, string fallbackSuffix)
    {
        var cleaned = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var segments = cleaned.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()))
            .ToList();
        var fileName = segments.Count > 0 ? segments[^1] : "";
        if (fileName.Length == 0) fileName = "entry" + fallbackSuffix;
        else if (!Path.HasExtension(fileName)) fileName += fallbackSuffix;
        fileName = $"{index:D4}_{fileName}";
        return segments.Count > 1
            ? Path.Combine(string.Join(Path.DirectorySeparatorChar, segments.Take(segments.Count - 1)), fileName)
            : fileName;
    }
}
