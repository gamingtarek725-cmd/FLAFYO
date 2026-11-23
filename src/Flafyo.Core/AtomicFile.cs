using System.Text;

namespace Flafyo.Core;

/// <summary>
/// Writes user data through a temporary file in the destination directory, then replaces the target.
/// Keeping the temporary file beside the target keeps the final move on the same volume.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents, bool keepBackup = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            PreserveBackup(target, keepBackup);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static async Task WriteAllTextAsync(
        string path,
        string contents,
        bool keepBackup = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            PreserveBackup(target, keepBackup);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void PreserveBackup(string target, bool keepBackup)
    {
        if (keepBackup && File.Exists(target))
            File.Copy(target, target + ".bak", overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup must not hide the original write or replacement error.
        }
    }
}
