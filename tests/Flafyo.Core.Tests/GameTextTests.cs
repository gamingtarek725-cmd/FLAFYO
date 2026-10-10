using System.Text;
using Flafyo.Core;
using Flafyo.Core.GameText;
using Xunit;

namespace Flafyo.Core.Tests;

public class GameStringFilterTests
{
    [Theory]
    [InlineData("Start Game", true)]
    [InlineData("Cancel", true)]
    [InlineData("Hello {0}, welcome back!", true)]
    [InlineData("You have 5 coins", true)]
    [InlineData("btn_start", false)]
    [InlineData("PlayerController", false)]
    [InlineData("Assets/ui/btn.png", false)]
    [InlineData("https://example.com/a b", false)]
    [InlineData("12345", false)]
    [InlineData("OK", false)]
    [InlineData("true", false)]
    [InlineData("مرحبا بك في اللعبة", false)]
    public void Only_human_text_is_translatable(string s, bool expected) =>
        Assert.Equal(expected, GameStringFilter.LooksTranslatable(s));
}

public class PlaceholderGuardTests
{
    [Fact]
    public void Codes_are_hidden_from_the_model_and_restored()
    {
        var r = PlaceholderGuard.TranslateProtected("You have {0} coins", s => s == "You have [1] coins" ? "لديك [1] عملات" : "?");
        Assert.Equal("لديك {0} عملات", r);
    }

    [Fact]
    public void Printf_codes_and_arabic_indic_markers_work()
    {
        var r = PlaceholderGuard.TranslateProtected("Hello %s!", s => "مرحبا [١]!");
        Assert.Equal("مرحبا %s!", r);
    }

    [Fact]
    public void When_the_model_loses_a_code_each_piece_is_translated_separately()
    {
        var r = PlaceholderGuard.TranslateProtected("You have {0} coins", s => s.Contains("[1]") ? "عملات" : "AR(" + s + ")");
        Assert.Equal("AR(You have) {0} AR(coins)", r);
    }

    [Fact]
    public void Line_breaks_are_kept_and_lines_translated_separately()
    {
        Assert.Equal("AR:Hello there\nAR:Good luck", PlaceholderGuard.TranslateProtected("Hello there\nGood luck", s => "AR:" + s));
        Assert.Equal(@"AR:Line one\nAR:Line two", PlaceholderGuard.TranslateProtected(@"Line one\nLine two", s => "AR:" + s));
    }

    [Fact]
    public void Text_with_only_codes_is_left_alone() =>
        Assert.Equal("{0}", PlaceholderGuard.TranslateProtected("{0}", s => "SHOULD NOT BE CALLED"));
}

public class ArabicShaperTests
{
    [Fact]
    public void Letters_are_joined_with_lam_alef_ligature() =>
        Assert.Equal("\uFEB3\uFEFC\uFEE1", ArabicShaper.Shape("سلام"));

    [Fact]
    public void Joining_forms_follow_the_neighbours() =>
        Assert.Equal("\uFEDB\uFE98\uFE8E\uFE8F", ArabicShaper.Shape("كتاب"));

    [Fact]
    public void Visual_order_reverses_arabic_but_keeps_latin_and_numbers_readable()
    {
        Assert.Equal("\uFEE1\uFEFC\uFEB3", ArabicShaper.ToVisual("سلام"));
        Assert.Equal("\uFEE1\uFEFC\uFEB3 12 ab", ArabicShaper.ToVisual("ab 12 سلام"));
    }

    [Fact]
    public void Codes_survive_shaping() =>
        Assert.Equal("{0} \uFEE1\uFEFC\uFEB3", ArabicShaper.ToVisualProtected("سلام {0}"));
}

public class GameFormatTests
{
    private static string? Ar(string s) => "AR:" + s;

    [Fact]
    public void Json_values_are_translated_but_keys_ids_and_comments_are_not()
    {
        var input = "{\"title\": \"Start Game\", \"id\": \"btn_start\", \"lines\": [\"Hello there friend\", \"OK\"], \"n\": 5 /* c */}";
        var expected = "{\"title\": \"AR:Start Game\", \"id\": \"btn_start\", \"lines\": [\"AR:Hello there friend\", \"OK\"], \"n\": 5 /* c */}";
        Assert.Equal(expected, new JsonTextFormat().Transform(input, Ar));
        Assert.Equal("{\"Start Game\": \"AR:Begin now\"}", new JsonTextFormat().Transform("{\"Start Game\": \"Begin now\"}", Ar));
    }

    [Fact]
    public void Json_escapes_quotes_in_the_result() =>
        Assert.Equal("{\"t\": \"AR:Say \\\"hi\\\" to everyone\"}",
            new JsonTextFormat().Transform("{\"t\": \"Say \\\"hi\\\" to everyone\"}", Ar));

    [Fact]
    public void Po_empty_translations_are_filled_and_existing_ones_kept()
    {
        var input = "msgid \"\"\nmsgstr \"\"\n\"Project-Id-Version: x\\n\"\n\nmsgid \"Hello there friend\"\nmsgstr \"\"\n\nmsgid \"Already done\"\nmsgstr \"تم\"\n\nmsgid \"One coin\"\nmsgid_plural \"Many coins\"\nmsgstr[0] \"\"\nmsgstr[1] \"\"\n";
        var r = new PoTextFormat().Transform(input, Ar);
        Assert.Contains("msgstr \"AR:Hello there friend\"", r);
        Assert.Contains("msgstr \"تم\"", r);
        Assert.Contains("msgstr[0] \"AR:One coin\"", r);
        Assert.Contains("msgstr[1] \"AR:Many coins\"", r);
        Assert.Contains("\"Project-Id-Version: x\\n\"", r);
    }

    [Fact]
    public void KeyValue_files_keep_comments_numbers_and_quotes()
    {
        var input = "# comment\ngreeting = Hello there friend\nmax_hp=100\ntitle=\"Main Menu\"\n[Section]\n";
        var expected = "# comment\ngreeting = AR:Hello there friend\nmax_hp=100\ntitle=\"AR:Main Menu\"\n[Section]\n";
        Assert.Equal(expected, new KeyValueTextFormat().Transform(input, Ar));
    }

    [Fact]
    public void Srt_text_is_translated_and_timing_is_kept()
    {
        var input = "1\n00:00:01,000 --> 00:00:03,000\nHello there\nmy friend\n\n2\n00:00:04,000 --> 00:00:05,000\n<i>Run!</i>\n";
        var r = new SrtTextFormat().Transform(input, Ar);
        Assert.Contains("00:00:01,000 --> 00:00:03,000\nAR:Hello there my friend", r);
        Assert.Contains("<i>Run!</i>", r);
    }

    [Fact]
    public void Xml_text_attributes_and_entities()
    {
        var input = "<items><item id=\"1\" title=\"Iron Sword\"><name>Iron Sword</name><desc>A sturdy blade &amp; shield</desc><price>100</price></item></items>";
        var r = new XmlTextFormat().Transform(input, Ar);
        Assert.Contains("id=\"1\" title=\"AR:Iron Sword\"", r);
        Assert.Contains("<name>AR:Iron Sword</name>", r);
        Assert.Contains("<desc>AR:A sturdy blade &amp; shield</desc>", r);
        Assert.Contains("<price>100</price>", r);
    }

    [Fact]
    public void Csv_translates_only_the_text_column_and_keeps_quoting()
    {
        var input = "key,en,ar\nhello,\"Hello there, friend\",\nbye,Goodbye my friend,\n";
        var expected = "key,en,ar\nhello,\"AR:Hello there, friend\",\nbye,AR:Goodbye my friend,\n";
        Assert.Equal(expected, new CsvTextFormat().Transform(input, Ar));
    }

    [Fact]
    public void Formats_are_chosen_by_extension()
    {
        Assert.IsType<JsonTextFormat>(GameFormats.For("a/en.json", false));
        Assert.IsType<PoTextFormat>(GameFormats.For("a/en.po", false));
        Assert.Null(GameFormats.For("a/game.exe", true));
        Assert.Null(GameFormats.For("a/readme.txt", false));
        Assert.IsType<PlainTextFormat>(GameFormats.For("a/readme.txt", true));
    }
}

public class TextFileIOTests
{
    [Fact]
    public void Legacy_code_page_is_detected_and_switches_to_utf8_for_arabic()
    {
        var d = TextFileIO.Decode(new byte[] { 0x43, 0x61, 0x66, 0xE9 });
        Assert.Equal("Café", d.Text);
        Assert.Equal(Encoding.UTF8.GetBytes("مرحبا"), TextFileIO.Encode("مرحبا", d));
        Assert.Equal(new byte[] { 0x43, 0x61, 0x66, 0xE9 }, TextFileIO.Encode("Café", d));
    }

    [Fact]
    public void Utf8_bom_is_preserved()
    {
        var d = TextFileIO.Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' });
        Assert.Equal("hi", d.Text);
        var bytes = TextFileIO.Encode("مرحبا", d);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }
}

public class GameFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flafyo-test-" + Guid.NewGuid().ToString("N"));
    public GameFolderTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string MakeGame()
    {
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(game, "lang")); Directory.CreateDirectory(Path.Combine(game, "data")); Directory.CreateDirectory(Path.Combine(game, "bin"));
        File.WriteAllText(Path.Combine(game, "lang", "en.json"), "{\"greet\":\"Hello there friend\",\"id\":\"npc_01\"}");
        File.WriteAllText(Path.Combine(game, "data", "items.xml"), "<items><item><name>Iron Sword</name></item></items>");
        File.WriteAllBytes(Path.Combine(game, "bin", "game.exe"), new byte[] { 0x4D, 0x5A, 0x00 });
        File.WriteAllText(Path.Combine(game, "readme.txt"), "Please read this file carefully");
        return game;
    }

    private static (TranslationResolver, FakeTranslator) Resolver()
    {
        var tr = new FakeTranslator();
        return (new TranslationResolver(tr, new TranslationCache()), tr);
    }

    [Fact]
    public void Scan_finds_only_text_files_with_translatable_strings()
    {
        var game = MakeGame();
        var files = GameScanner.Scan(game, null, CancellationToken.None);
        Assert.Equal(new[] { Path.Combine("data", "items.xml"), Path.Combine("lang", "en.json") }, files.Select(f => f.RelativePath).ToArray());
        Assert.All(files, f => Assert.Equal(1, f.StringCount));
        Assert.Equal(3, GameScanner.Scan(game, new GameScanOptions { IncludePlainText = true }, CancellationToken.None).Count);
    }

    [Fact]
    public async Task Translated_copies_are_written_and_the_run_can_be_resumed_from_memory()
    {
        var game = MakeGame();
        var files = GameScanner.Scan(game, null, CancellationToken.None);
        var output = Path.Combine(_root, "out");

        var (resolver, tr) = Resolver();
        var report = await new GameTextTranslator(resolver).TranslateAsync(files, output, null, null, CancellationToken.None);
        Assert.Equal(2, report.Files); Assert.Equal(2, report.Strings); Assert.Equal(0, report.Errors);
        Assert.Equal("{\"greet\":\"AR:Hello there friend\",\"id\":\"npc_01\"}", File.ReadAllText(Path.Combine(output, "lang", "en.json")));
        Assert.Contains("<name>AR:Iron Sword</name>", File.ReadAllText(Path.Combine(output, "data", "items.xml")));
        Assert.Contains("Hello there friend", File.ReadAllText(Path.Combine(game, "lang", "en.json")));   // original untouched
        Assert.True(File.Exists(Path.Combine(output, "_flafyo_memory.json")));

        var (resolver2, tr2) = Resolver();
        var again = await new GameTextTranslator(resolver2).TranslateAsync(files, output, null, null, CancellationToken.None);
        Assert.Equal(2, again.FromMemory);
        Assert.Equal(0, tr2.Calls);
    }

    [Fact]
    public async Task Arabic_shaping_option_changes_the_output()
    {
        var game = MakeGame();
        var files = GameScanner.Scan(game, null, CancellationToken.None);
        var tr = new PostProcessTranslator(new FakeTranslator(), _ => "سلام");
        var resolver = new TranslationResolver(tr, new TranslationCache());
        var output = Path.Combine(_root, "shaped");
        await new GameTextTranslator(resolver).TranslateAsync(files, output, new GameTranslateOptions { ShapeArabic = true }, null, CancellationToken.None);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "lang", "en.json")));
        Assert.Equal("\uFEE1\uFEFC\uFEB3", doc.RootElement.GetProperty("greet").GetString());
    }

    [Fact]
    public void Apply_backs_up_the_originals_and_restore_brings_them_back()
    {
        var game = Path.Combine(_root, "g"); var translated = Path.Combine(_root, "t");
        Directory.CreateDirectory(Path.Combine(game, "lang")); Directory.CreateDirectory(Path.Combine(translated, "lang"));
        File.WriteAllText(Path.Combine(game, "lang", "en.json"), "orig");
        File.WriteAllText(Path.Combine(translated, "lang", "en.json"), "new");
        File.WriteAllText(Path.Combine(translated, "lang", "extra.json"), "not in game");
        File.WriteAllText(Path.Combine(translated, "_flafyo_memory.json"), "{}");

        Assert.Equal(1, GameInstaller.Apply(translated, game));
        Assert.Equal("new", File.ReadAllText(Path.Combine(game, "lang", "en.json")));
        Assert.False(File.Exists(Path.Combine(game, "lang", "extra.json")));

        File.WriteAllText(Path.Combine(translated, "lang", "en.json"), "newer");
        GameInstaller.Apply(translated, game);                                                       // second apply keeps the FIRST backup
        Assert.Equal("orig", File.ReadAllText(Path.Combine(GameInstaller.BackupRoot(game), "lang", "en.json")));

        Assert.Equal(1, GameInstaller.Restore(game));
        Assert.Equal("orig", File.ReadAllText(Path.Combine(game, "lang", "en.json")));
    }

    [Fact]
    public void Drive_roots_are_refused() =>
        Assert.Throws<InvalidOperationException>(() => GameInstaller.Apply(_root, Path.GetPathRoot(Path.GetTempPath())!));
}
