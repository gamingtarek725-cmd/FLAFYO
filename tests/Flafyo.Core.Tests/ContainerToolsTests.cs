using System.IO.Compression;
using System.Text;
using Flafyo.Core.GameText;
using Xunit;

namespace Flafyo.Core.Tests;

public class ContainerToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flafyo-container-" + Guid.NewGuid().ToString("N"));
    public ContainerToolsTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static ArcEntry Template(string name, uint type)
    {
        var raw = new byte[64]; Encoding.Latin1.GetBytes(name).CopyTo(raw, 0);
        return new ArcEntry { RawName = raw, Name = name, TypeHash = type, SizeAndFlags = 0x40000000 };
    }

    [Fact]
    public void Arc_builder_rejects_more_than_the_supported_entry_count()
    {
        var tooMany = Enumerable.Range(0, 10_001)
            .Select(i => (Template($"entry{i}.bin", 0x1234ABCD), Array.Empty<byte>()))
            .ToArray();

        Assert.Throws<InvalidDataException>(() => ArcArchive.Build(7, tooMany));
    }

    [Fact]
    public void Arc_container_round_trips_through_extract_edit_repack()
    {
        var text = Encoding.UTF8.GetBytes("{\"greet\":\"Hello there friend\"}");
        var arcBytes = ArcArchive.Build(7, new[] { (Template(@"data\id\eng\msg.json", 0x1234ABCD), text) });
        var arcPath = Path.Combine(_root, "lang.arc");
        File.WriteAllBytes(arcPath, arcBytes);

        var (kind, count) = ContainerTools.Extract(arcPath, Path.Combine(_root, "x"));
        Assert.Equal(ContainerKind.Arc, kind); Assert.Equal(1, count);

        var extractedFile = Directory.GetFiles(Path.Combine(_root, "x"), "*.json", SearchOption.AllDirectories)[0];
        Assert.Equal("{\"greet\":\"Hello there friend\"}", File.ReadAllText(extractedFile));
        File.WriteAllText(extractedFile, "{\"greet\":\"مرحبا بك يا صديقي\"}");

        var rebuilt = ContainerTools.Repack(Path.Combine(_root, "x"));
        var arc2 = ArcArchive.Parse(rebuilt);
        Assert.Equal(@"data\id\eng\msg.json", arc2.Entries[0].Name);                    // name/path preserved
        Assert.Equal(0x1234ABCDu, arc2.Entries[0].TypeHash);                            // type preserved
        Assert.Equal("{\"greet\":\"مرحبا بك يا صديقي\"}", Encoding.UTF8.GetString(arc2.Read(arc2.Entries[0])));

        // Full round-trip: write the rebuilt ARC, extract it again, and verify the preserved path and exact bytes.
        var rebuiltArcPath = Path.Combine(_root, "rebuilt.arc");
        File.WriteAllBytes(rebuiltArcPath, rebuilt);
        var (rebuiltKind, rebuiltCount) = ContainerTools.Extract(rebuiltArcPath, Path.Combine(_root, "arc-roundtrip"));
        Assert.Equal(ContainerKind.Arc, rebuiltKind);
        Assert.Equal(1, rebuiltCount);
        var roundTripFile = Directory.GetFiles(Path.Combine(_root, "arc-roundtrip"), "*.json", SearchOption.AllDirectories).Single();
        Assert.Equal("{\"greet\":\"مرحبا بك يا صديقي\"}", File.ReadAllText(roundTripFile));

        var backup = ContainerTools.Apply(arcPath, rebuilt);
        Assert.True(File.Exists(backup));
        Assert.Equal(arcBytes, File.ReadAllBytes(backup));                              // the ORIGINAL is what got backed up
        Assert.Equal(rebuilt, File.ReadAllBytes(arcPath));
        Assert.True(ContainerTools.Restore(arcPath));
        Assert.Equal(arcBytes, File.ReadAllBytes(arcPath));
    }

    [Fact]
    public void Apply_rejects_invalid_rebuild_without_changing_original_or_creating_backup()
    {
        var original = Path.Combine(_root, "original.arc");
        var valid = ArcArchive.Build(7, new[] { (Template("data.bin", 0x1234ABCD), Encoding.UTF8.GetBytes("original")) });
        File.WriteAllBytes(original, valid);

        Assert.Throws<InvalidDataException>(() => ContainerTools.Apply(original, Encoding.UTF8.GetBytes("not an archive")));

        Assert.Equal(valid, File.ReadAllBytes(original));
        Assert.False(File.Exists(original + ".flafyo_backup"));
    }

    [Fact]
    public void Pak_file_with_zip_bytes_is_detected_and_round_trips()
    {
        var zipPath = Path.Combine(_root, "data3.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("data/lang/en.json");
            using var s = e.Open(); using var w = new StreamWriter(s);
            w.Write("{\"line\":\"Find the key\"}");
        }
        var pakPath = Path.Combine(_root, "data3.pak");                                 // same bytes, different extension — like Dying Light
        File.Copy(zipPath, pakPath);

        var (kind, count) = ContainerTools.Extract(pakPath, Path.Combine(_root, "y"));
        Assert.Equal(ContainerKind.Zip, kind); Assert.Equal(1, count);

        var inner = Path.Combine(_root, "y", "data", "lang", "en.json");
        Assert.True(File.Exists(inner));
        File.WriteAllText(inner, "{\"line\":\"جد المفتاح\"}");

        var rebuilt = ContainerTools.Repack(Path.Combine(_root, "y"));
        using (var check = new ZipArchive(new MemoryStream(rebuilt), ZipArchiveMode.Read))
        {
            using var rs = check.GetEntry("data/lang/en.json")!.Open(); using var rr = new StreamReader(rs);
            Assert.Equal("{\"line\":\"جد المفتاح\"}", rr.ReadToEnd());
        }

        // Re-open through FLAFYO's extractor, not just ZipArchive, to verify the complete workflow.
        var rebuiltZipPath = Path.Combine(_root, "rebuilt.pak");
        File.WriteAllBytes(rebuiltZipPath, rebuilt);
        var (rebuiltKind, rebuiltCount) = ContainerTools.Extract(rebuiltZipPath, Path.Combine(_root, "zip-roundtrip"));
        Assert.Equal(ContainerKind.Zip, rebuiltKind);
        Assert.Equal(1, rebuiltCount);
        Assert.Equal("{\"line\":\"جد المفتاح\"}",
            File.ReadAllText(Path.Combine(_root, "zip-roundtrip", "data", "lang", "en.json")));
    }

    [Fact]
    public void Zip_extraction_rejects_parent_directory_traversal()
    {
        var archive = Path.Combine(_root, "unsafe.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("must not escape");
        }
        var output = Path.Combine(_root, "safe-output");
        Assert.Throws<InvalidDataException>(() => ContainerTools.Extract(archive, output));
        Assert.False(Directory.Exists(output)); // failed extraction must not leave a partial destination folder
        Assert.False(Directory.EnumerateDirectories(_root, ".flafyo-extract-*").Any());
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Fact]
    public void Extraction_refuses_nonempty_destination_without_touching_existing_files()
    {
        var archive = Path.Combine(_root, "valid.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("safe.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("archive data");
        }

        var output = Path.Combine(_root, "existing");
        Directory.CreateDirectory(output);
        var marker = Path.Combine(output, "keep.txt");
        File.WriteAllText(marker, "user data");

        Assert.Throws<IOException>(() => ContainerTools.Extract(archive, output));
        Assert.Equal("user data", File.ReadAllText(marker));
        Assert.False(File.Exists(Path.Combine(output, "safe.txt")));
    }

    [Fact]
    public void Zip_repack_rejects_tampered_index_paths()
    {
        var folder = Path.Combine(_root, "tampered");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "_flafyo_container.txt"),
            new[] { "FLAFYO_CONTAINER_V1", "Zip", "../outside.txt" });
        Assert.Throws<InvalidDataException>(() => ContainerTools.Repack(folder));
    }

    [Fact]
    public void Non_container_bytes_are_rejected() =>
        Assert.Throws<InvalidDataException>(() => ContainerTools.Extract(WriteRandom(), Path.Combine(_root, "z")));

    private string WriteRandom()
    {
        var p = Path.Combine(_root, "notes.txt");
        File.WriteAllText(p, "just some plain file, not an archive");
        return p;
    }
}
