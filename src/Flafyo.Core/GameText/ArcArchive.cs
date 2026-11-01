using System.IO.Compression;

namespace Flafyo.Core.GameText;

/// <summary>One file inside an MT Framework ".arc" archive (Capcom games such as Resident Evil 6).</summary>
public sealed class ArcEntry
{
    public byte[] RawName { get; init; } = new byte[64];
    public string Name { get; init; } = "";
    public uint TypeHash { get; init; }
    public uint CompressedSize { get; init; }
    /// <summary>Lower 29 bits = real size, upper 3 bits = flags.</summary>
    public uint SizeAndFlags { get; init; }
    public uint Offset { get; init; }

    public int Size => (int)(SizeAndFlags & 0x1FFFFFFF);
    public uint Flags => SizeAndFlags & 0xE0000000;
    public bool IsTexture => TypeHash == 0x241F5DEB;
    public string TypeName => TypeHash == 0x241F5DEB ? "texture" : "0x" + TypeHash.ToString("X8");
}

/// <summary>
/// Reader/writer for the ARC container: "ARC\0", version, entry count, 80-byte entries (name[64], type hash, compressed size,
/// size+flags, offset), data (zlib) starting at the next 0x8000 boundary. Layout verified on a real Resident Evil 6 archive.
/// </summary>
public sealed class ArcArchive
{
    public const int DataAlignment = 0x8000;
    private readonly byte[] _file;

    public ushort Version { get; }
    public IReadOnlyList<ArcEntry> Entries { get; }

    private ArcArchive(byte[] file, ushort version, List<ArcEntry> entries) { _file = file; Version = version; Entries = entries; }

    public static bool IsArc(ReadOnlySpan<byte> d) => d.Length >= 8 && d[0] == 'A' && d[1] == 'R' && d[2] == 'C' && d[3] == 0;

    public static ArcArchive Parse(byte[] file)
    {
        if (!IsArc(file)) throw new InvalidDataException("Not an ARC archive.");
        ushort version = BitConverter.ToUInt16(file, 4), count = BitConverter.ToUInt16(file, 6);
        if (8L + 80L * count > file.Length) throw new InvalidDataException("ARC header is damaged.");
        var list = new List<ArcEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int p = 8 + i * 80;
            var raw = new byte[64];
            Buffer.BlockCopy(file, p, raw, 0, 64);
            int len = Array.IndexOf(raw, (byte)0); if (len < 0) len = 64;
            var e = new ArcEntry
            {
                RawName = raw,
                Name = System.Text.Encoding.Latin1.GetString(raw, 0, len),
                TypeHash = BitConverter.ToUInt32(file, p + 64),
                CompressedSize = BitConverter.ToUInt32(file, p + 68),
                SizeAndFlags = BitConverter.ToUInt32(file, p + 72),
                Offset = BitConverter.ToUInt32(file, p + 76),
            };
            if ((long)e.Offset + e.CompressedSize > file.Length) throw new InvalidDataException($"Entry {i} points outside the file.");
            list.Add(e);
        }
        return new ArcArchive(file, version, list);
    }

    /// <summary>The unpacked bytes of an entry.</summary>
    public byte[] Read(ArcEntry e)
    {
        var slice = new byte[e.CompressedSize];
        Buffer.BlockCopy(_file, (int)e.Offset, slice, 0, slice.Length);
        try
        {
            using var input = new MemoryStream(slice);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(e.Size > 0 ? e.Size : 4096);
            z.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException) { return slice; }     // stored without compression
    }

    /// <summary>Builds an archive. Each entry keeps its name, type and flags; its data is compressed again.</summary>
    public static byte[] Build(ushort version, IReadOnlyList<(ArcEntry Template, byte[] Data)> files)
    {
        int headerEnd = 8 + 80 * files.Count;
        long dataStart = ((long)headerEnd + DataAlignment - 1) / DataAlignment * DataAlignment;

        var packed = new List<byte[]>();
        foreach (var (_, data) in files)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
            packed.Add(ms.ToArray());
        }

        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);
        w.Write((byte)'A'); w.Write((byte)'R'); w.Write((byte)'C'); w.Write((byte)0);
        w.Write(version); w.Write((ushort)files.Count);
        long offset = dataStart;
        for (int i = 0; i < files.Count; i++)
        {
            var (t, data) = files[i];
            var name = new byte[64];
            Buffer.BlockCopy(t.RawName, 0, name, 0, Math.Min(64, t.RawName.Length));
            w.Write(name);
            w.Write(t.TypeHash);
            w.Write((uint)packed[i].Length);
            w.Write(t.Flags | ((uint)data.Length & 0x1FFFFFFF));
            w.Write((uint)offset);
            offset += packed[i].Length;
        }
        w.Write(new byte[dataStart - output.Length]);          // padding up to the first data block
        foreach (var p in packed) w.Write(p);
        w.Flush();
        return output.ToArray();
    }
}
