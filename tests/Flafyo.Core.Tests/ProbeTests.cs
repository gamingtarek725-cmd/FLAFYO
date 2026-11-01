using System.Text;
using Flafyo.Core.GameText;
using Xunit;

namespace Flafyo.Core.Tests;

public class ArcAndProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flafyo-probe-" + Guid.NewGuid().ToString("N"));
    public ArcAndProbeTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static ArcEntry Template(string name, uint type, uint flags = 0x40000000)
    {
        var raw = new byte[64]; Encoding.Latin1.GetBytes(name).CopyTo(raw, 0);
        return new ArcEntry { RawName = raw, Name = name, TypeHash = type, SizeAndFlags = flags };
    }

    private static byte[] TextBlob() => Encoding.ASCII.GetBytes(string.Join("\0", new[]
    {
        "Find the golden key before midnight.", "Open the door very carefully now.", "The guard is waiting near the gate.",
        "We must leave this place at once.", "Nobody can hear you out here.", "Take the old road to the village.",
    }));

    [Fact]
    public void Arc_round_trip_keeps_names_types_flags_and_data()
    {
        var a = new byte[5000]; new Random(1).NextBytes(a);
        var text = TextBlob();
        var bytes = ArcArchive.Build(7, new[] { (Template(@"data\id\eng\pic", 0x241F5DEB), a), (Template(@"data\id\eng\msg", 0x1234ABCD), text) });

        Assert.True(ArcArchive.IsArc(bytes));
        var arc = ArcArchive.Parse(bytes);
        Assert.Equal(7, arc.Version);
        Assert.Equal(2, arc.Entries.Count);
        Assert.Equal(@"data\id\eng\pic", arc.Entries[0].Name);
        Assert.True(arc.Entries[0].IsTexture);
        Assert.Equal(0x40000000u, arc.Entries[1].Flags);
        Assert.Equal(text.Length, arc.Entries[1].Size);
        Assert.Equal(a, arc.Read(arc.Entries[0]));
        Assert.Equal(text, arc.Read(arc.Entries[1]));
        Assert.Equal((uint)ArcArchive.DataAlignment, arc.Entries[0].Offset);       // data starts at the 0x8000 boundary like the real files
    }

    [Fact]
    public void Arc_parse_rejects_garbage_and_truncated_files()
    {
        Assert.Throws<InvalidDataException>(() => ArcArchive.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        var bytes = ArcArchive.Build(7, new[] { (Template("x", 1), TextBlob()) });
        Assert.Throws<InvalidDataException>(() => ArcArchive.Parse(bytes[..200]));
    }

    [Fact]
    public void Miner_finds_ascii_and_utf16_sentences_but_not_noise()
    {
        var noise = new byte[3000]; new Random(5).NextBytes(noise);
        var data = noise.Concat(Encoding.ASCII.GetBytes("Find the golden key before midnight.")).Concat(new byte[] { 0, 1, 2 })
                        .Concat(Encoding.Unicode.GetBytes("Nobody can hear you out here.")).Concat(noise).ToArray();
        var found = BinaryTextMiner.Extract(data).Select(f => f.Text).ToList();
        Assert.Contains("Find the golden key before midnight.", found);
        Assert.Contains("Nobody can hear you out here.", found);
        Assert.True(found.Count < 10, "random bytes must not look like text: " + found.Count);
    }

    [Fact]
    public void Probe_ranks_files_and_archive_entries_and_ignores_pictures_and_media()
    {
        var pic = new byte[8000]; new Random(2).NextBytes(pic);
        File.WriteAllBytes(Path.Combine(_root, "lang_eng.arc"),
            ArcArchive.Build(7, new[] { (Template("data\\pic", 0x241F5DEB), pic), (Template("data\\messages", 0x5555AAAA), TextBlob()) }));
        File.WriteAllBytes(Path.Combine(_root, "movie.mp4"), TextBlob());                      // media is skipped even if it has text-like bytes
        File.WriteAllBytes(Path.Combine(_root, "pure.bin"), pic);

        var hits = GameProbe.Scan(_root, CancellationToken.None);
        Assert.Single(hits);
        Assert.Equal("ARC", hits[0].Kind);
        Assert.Contains("messages", hits[0].Entry);
        Assert.True(hits[0].Sentences >= 5);

        var exported = GameProbe.Export(hits[0], Path.Combine(_root, "out"));
        Assert.Contains("Find the golden key before midnight.", File.ReadAllText(exported));

        var count = GameProbe.ExtractArc(Path.Combine(_root, "lang_eng.arc"), Path.Combine(_root, "unpacked"));
        Assert.Equal(2, count);
        Assert.True(File.Exists(Path.Combine(_root, "unpacked", "_index.txt")));
    }
}
