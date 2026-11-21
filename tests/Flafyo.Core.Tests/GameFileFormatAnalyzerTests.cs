using System.IO.Compression;
using System.Text;
using Flafyo.Core.GameText;
using Xunit;

namespace Flafyo.Core.Tests;

public sealed class GameFileFormatAnalyzerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flafyo-format-" + Guid.NewGuid().ToString("N"));

    public GameFileFormatAnalyzerTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Recognizes_a_supported_json_text_file_without_changing_it()
    {
        var path = Path.Combine(_root, "en.json");
        const string source = "{\"title\":\"Start Game\",\"id\":\"menu_start\"}";
        File.WriteAllText(path, source, new UTF8Encoding(false));

        var report = GameFileFormatAnalyzer.Analyze(path);

        Assert.Contains("JSON", report.Kind);
        Assert.Equal("utf-8", report.Encoding);
        Assert.Contains(report.Samples, s => s.Contains("title"));
        Assert.Equal(source, File.ReadAllText(path));
    }

    [Fact]
    public void Recognizes_zip_by_signature_even_when_extension_is_pak()
    {
        var path = Path.Combine(_root, "assets.pak");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("localization/en.json").Open()))
            writer.Write("{\"hello\":\"Start Game\"}");

        var report = GameFileFormatAnalyzer.Analyze(path);

        Assert.Contains("ZIP", report.Kind);
        Assert.True(report.EntryCount > 0);
        Assert.Contains(report.Samples, s => s.Contains("localization/en.json"));
    }

    [Fact]
    public void Does_not_guess_an_unknown_binary_extension()
    {
        var path = Path.Combine(_root, "arabic_win32.chars");
        File.WriteAllBytes(path, new byte[] { 0, 255, 0, 128, 1, 0, 255, 10, 0, 33, 0, 99, 0, 1, 0, 255 });

        var report = GameFileFormatAnalyzer.Analyze(path);

        Assert.Contains("غير معروف", report.Kind);
        Assert.Contains(report.Warnings, w => w.Contains("محوّلًا"));
    }

    [Fact]
    public void Detects_the_capcom_arc_signature_and_lists_entries()
    {
        var path = Path.Combine(_root, "sample.arc");
        var bytes = new byte[88];
        bytes[0] = (byte)'A'; bytes[1] = (byte)'R'; bytes[2] = (byte)'C'; bytes[3] = 0;
        BitConverter.GetBytes((ushort)7).CopyTo(bytes, 4);
        BitConverter.GetBytes((ushort)0).CopyTo(bytes, 6);
        File.WriteAllBytes(path, bytes);

        var report = GameFileFormatAnalyzer.Analyze(path);

        Assert.Contains("ARC", report.Kind);
        Assert.Equal(0, report.EntryCount);
    }
}
