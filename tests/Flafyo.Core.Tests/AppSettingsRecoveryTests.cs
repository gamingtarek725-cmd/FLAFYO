using System.Text.Json;
using Flafyo.Desktop;
using Xunit;

namespace Flafyo.Core.Tests;

public sealed class AppSettingsRecoveryTests
{
    [Fact]
    public void Invalid_settings_are_restored_from_last_good_backup()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var backup = new AppSettings { SpeedIndex = 2, Masculine = false };
            File.WriteAllText(path, "{broken json");
            File.WriteAllText(path + ".bak", JsonSerializer.Serialize(backup));

            var loaded = AppSettings.LoadFrom(path);

            Assert.Equal(2, loaded.SpeedIndex);
            Assert.False(loaded.Masculine);
            var repaired = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            Assert.NotNull(repaired);
            Assert.Equal(2, repaired!.SpeedIndex);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Valid_primary_settings_take_precedence_over_backup()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new AppSettings { SpeedIndex = 0 }));
            File.WriteAllText(path + ".bak", JsonSerializer.Serialize(new AppSettings { SpeedIndex = 2 }));

            var loaded = AppSettings.LoadFrom(path);

            Assert.Equal(0, loaded.SpeedIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Missing_or_invalid_settings_and_backup_return_defaults()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path + ".bak", "{invalid");

            var loaded = AppSettings.LoadFrom(path);

            Assert.Equal(1, loaded.SpeedIndex);
            Assert.True(loaded.Masculine);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Island_preferences_have_safe_defaults_and_survive_json_roundtrip()
    {
        var original = new AppSettings();
        Assert.True(original.Style.IslandEnabled);
        Assert.True(original.Style.IslandShowToggleButton);
        Assert.Equal(4.5, original.Style.IslandCollapseSeconds);

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original));
        Assert.NotNull(restored);
        Assert.True(restored!.Style.IslandEnabled);
        Assert.True(restored.Style.IslandShowToggleButton);
        Assert.Equal(4.5, restored.Style.IslandCollapseSeconds);
    }

    [Fact]
    public void Loading_a_game_profile_style_does_not_override_global_Island_preferences()
    {
        var global = new OverlayStyle
        {
            IslandEnabled = false,
            IslandShowToggleButton = false,
            IslandCollapseSeconds = 7
        };
        var game = new OverlayStyle
        {
            FontSize = 28,
            IslandEnabled = true,
            IslandShowToggleButton = true,
            IslandCollapseSeconds = 2.5
        };

        global.CopyFrom(game);

        Assert.Equal(28, global.FontSize);
        Assert.False(global.IslandEnabled);
        Assert.False(global.IslandShowToggleButton);
        Assert.Equal(7, global.IslandCollapseSeconds);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FLAFYO-SettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
