using System.Text.Json;
using Flafyo.Desktop;
using Xunit;

namespace Flafyo.Core.Tests;

public sealed class ProfileRecoveryTests
{
    [Fact]
    public void Damaged_game_profile_is_recovered_from_backup()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "game.json");
            var backup = new GameProfile
            {
                ExeName = "game",
                Title = "Recovered Game",
                SpeedIndex = 2
            };
            File.WriteAllText(path, "{damaged");
            File.WriteAllText(path + ".bak", JsonSerializer.Serialize(backup));

            var profiles = ProfileStore.LoadAllFrom(directory);

            var recovered = Assert.Single(profiles);
            Assert.Equal("game", recovered.ExeName);
            Assert.Equal("Recovered Game", recovered.Title);
            Assert.Equal(2, recovered.SpeedIndex);

            var repaired = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(path));
            Assert.NotNull(repaired);
            Assert.Equal("Recovered Game", repaired!.Title);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Invalid_profile_without_valid_backup_is_ignored()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "game.json"), "{damaged");

            Assert.Empty(ProfileStore.LoadAllFrom(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FLAFYO-ProfileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
