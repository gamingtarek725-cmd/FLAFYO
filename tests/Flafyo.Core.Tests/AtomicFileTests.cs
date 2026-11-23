using Flafyo.Core;
using Xunit;

namespace Flafyo.Core.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void Replacing_existing_file_preserves_previous_content_in_backup()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{\"version\":1}");

            AtomicFile.WriteAllText(path, "{\"version\":2}", keepBackup: true);

            Assert.Equal("{\"version\":2}", File.ReadAllText(path));
            Assert.Equal("{\"version\":1}", File.ReadAllText(path + ".bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void First_write_creates_parent_directory_without_a_backup()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "nested", "settings.json");

            AtomicFile.WriteAllText(path, "initial", keepBackup: true);

            Assert.Equal("initial", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".bak"));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Async_write_replaces_file_and_honors_cancellation()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "catalog.json");
            File.WriteAllText(path, "old");

            await AtomicFile.WriteAllTextAsync(path, "new");

            Assert.Equal("new", File.ReadAllText(path));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AtomicFile.WriteAllTextAsync(path, "must-not-appear", cancellationToken: cancellation.Token));
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "FLAFYO-AtomicFileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
