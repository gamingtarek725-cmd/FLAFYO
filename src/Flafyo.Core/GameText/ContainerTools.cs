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

    public static ContainerKind Detect(byte[] data)
    {
        if (ArcArchive.IsArc(data)) return ContainerKind.Arc;
        if (data.Length >= 4 && data[0] == 'P' && data[1] == 'K' && (data[2] == 3 || data[2] == 5 || data[2] == 7)) return ContainerKind.Zip;
        return ContainerKind.None;
    }

    /// <summary>Unpacks every entry into a folder, plus an index file that Repack needs to rebuild the same container later.</summary>
    public static (ContainerKind Kind, int Count) Extract(string containerPath, string outputFolder)
    {
        var data = File.ReadAllBytes(containerPath);
        var kind = Detect(data);
        Directory.CreateDirectory(outputFolder);
        var index = new List<string> { "FLAFYO_CONTAINER_V1", kind.ToString() };

        if (kind == ContainerKind.Arc)
        {
            var arc = ArcArchive.Parse(data);
            for (int i = 0; i < arc.Entries.Count; i++)
            {
                var e = arc.Entries[i];
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
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith('/') || e.Length == 0 && e.Name.Length == 0) continue;   // a folder entry
                var rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                var dest = Path.Combine(outputFolder, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using (var s = e.Open()) using (var f = File.Create(dest)) s.CopyTo(f);
                index.Add(rel.Replace(Path.DirectorySeparatorChar, '/'));
                n++;
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
            foreach (var line in lines.Skip(2))
            {
                var p = line.Split('\t');
                var raw = Convert.FromBase64String(p[1]);
                int len = Array.IndexOf(raw, (byte)0); if (len < 0) len = raw.Length;
                var template = new ArcEntry
                {
                    RawName = raw, Name = System.Text.Encoding.Latin1.GetString(raw, 0, len),
                    TypeHash = Convert.ToUInt32(p[2], 16), SizeAndFlags = Convert.ToUInt32(p[3], 16),
                };
                files.Add((template, File.ReadAllBytes(Path.Combine(extractedFolder, p[0]))));
            }
            return ArcArchive.Build(7, files);
        }
        if (kind == ContainerKind.Zip)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var rel in lines.Skip(2))
                {
                    var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                    using var s = entry.Open();
                    using var f = File.OpenRead(Path.Combine(extractedFolder, rel.Replace('/', Path.DirectorySeparatorChar)));
                    f.CopyTo(s);
                }
            return ms.ToArray();
        }
        throw new InvalidOperationException("نوع الحاوية غير مدعوم: " + kind);
    }

    /// <summary>Writes the rebuilt container over the original, after backing the original up next to it (once).</summary>
    public static string Apply(string originalContainerPath, byte[] rebuilt)
    {
        var backup = originalContainerPath + ".flafyo_backup";
        if (!File.Exists(backup)) File.Copy(originalContainerPath, backup);
        File.WriteAllBytes(originalContainerPath, rebuilt);
        return backup;
    }

    public static bool Restore(string originalContainerPath)
    {
        var backup = originalContainerPath + ".flafyo_backup";
        if (!File.Exists(backup)) return false;
        File.Copy(backup, originalContainerPath, overwrite: true);
        return true;
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
