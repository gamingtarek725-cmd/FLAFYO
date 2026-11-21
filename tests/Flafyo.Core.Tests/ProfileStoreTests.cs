using Flafyo.Desktop;
using Xunit;

namespace Flafyo.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flafyo-profile-" + Guid.NewGuid().ToString("N"));
    public ProfileStoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static GameProfile ValidProfile() => new()
    {
        ExeName = "sample-game",
        Title = "Sample Game",
        Width = 1280,
        Height = 720,
        Style = new OverlayStyle()
    };

    [Fact]
    public void Export_then_import_preserves_game_settings_and_style()
    {
        var expected = ValidProfile();
        expected.SpeedIndex = 2;
        expected.Continuous = false;
        expected.Style.FontSize = 28;
        expected.Style.TextColor = "#12ABEF";
        var path = Path.Combine(_root, "sample.flafyoprofile");

        ProfileStore.Export(expected, path);
        var actual = ProfileStore.Import(path);

        Assert.Equal(expected.ExeName, actual.ExeName);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.SpeedIndex, actual.SpeedIndex);
        Assert.Equal(expected.Continuous, actual.Continuous);
        Assert.Equal(expected.Style.FontSize, actual.Style.FontSize);
        Assert.Equal(expected.Style.TextColor, actual.Style.TextColor);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("folder\\game")]
    [InlineData("game name")]
    [InlineData("")]
    public void Import_rejects_unsafe_or_invalid_executable_names(string exe)
    {
        var profile = ValidProfile();
        profile.ExeName = exe;
        var path = Path.Combine(_root, "invalid.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(profile));

        Assert.Throws<InvalidDataException>(() => ProfileStore.Import(path));
    }

    [Fact]
    public void Import_rejects_profile_with_out_of_range_overlay_settings()
    {
        var profile = ValidProfile();
        profile.Style.BackOpacity = 500;
        var path = Path.Combine(_root, "invalid-style.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(profile));

        Assert.Throws<InvalidDataException>(() => ProfileStore.Import(path));
    }

    [Fact]
    public void Import_rejects_files_larger_than_256_kilobytes()
    {
        var path = Path.Combine(_root, "oversized.json");
        File.WriteAllBytes(path, new byte[256 * 1024 + 1]);

        Assert.Throws<InvalidDataException>(() => ProfileStore.Import(path));
    }

    [Fact]
    public void Import_rejects_malformed_json()
    {
        var path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "{ not json }");

        Assert.ThrowsAny<Exception>(() => ProfileStore.Import(path));
    }

    [Fact]
    public void Import_rejects_invalid_utf8()
    {
        var path = Path.Combine(_root, "invalid-utf8.json");
        File.WriteAllBytes(path, new byte[] { 0xC3, 0x28 });

        Assert.Throws<System.Text.DecoderFallbackException>(() => ProfileStore.Import(path));
    }
}
