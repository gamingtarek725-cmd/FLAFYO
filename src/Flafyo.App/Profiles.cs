using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Flafyo.Desktop;

/// <summary>Everything that should be remembered for one game.</summary>
public sealed class GameProfile
{
    public string ExeName { get; set; } = "";
    public string Title { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ModeIndex { get; set; }
    public int CaptureIndex { get; set; }
    public int MonitorIndex { get; set; }
    public int SpeedIndex { get; set; } = 1;
    public int TextSizeIndex { get; set; } = 1;
    public int PackIndex { get; set; }
    public bool Continuous { get; set; } = true;
    public bool Masculine { get; set; } = true;
    public bool Precise { get; set; }
    public bool AutoStart { get; set; }
    public OverlayStyle Style { get; set; } = new();
}

internal static class ProfileStore
{
    public static string Dir => Path.Combine(UserData.Dir, "profiles");

    private static string Safe(string exe) => new string(exe.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

    public static string DictionaryFile(string exe) => Path.Combine(UserData.Dir, $"dictionary.{Safe(exe)}.txt");
    public static string IdiomsFile(string exe) => Path.Combine(UserData.Dir, $"idioms.{Safe(exe)}.txt");

    public static List<GameProfile> LoadAll()
    {
        var list = new List<GameProfile>();
        try
        {
            if (!Directory.Exists(Dir)) return list;
            foreach (var f in Directory.EnumerateFiles(Dir, "*.json"))
            {
                try { var p = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(f)); if (p != null && p.ExeName.Length > 0) list.Add(p); }
                catch { }
            }
        }
        catch { }
        return list.OrderBy(p => p.ExeName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Save(GameProfile p)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, Safe(p.ExeName) + ".json"), JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete(string exe)
    {
        try { File.Delete(Path.Combine(Dir, Safe(exe) + ".json")); } catch { }
    }
}

internal static class ActiveWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "TextInputHost", "ApplicationFrameHost",
        "LockApp", "FLAFYO.GameTranslator", "dwm",
    };

    /// <summary>The program that owns the window in front (null when it is Windows itself or this app).</summary>
    public static string? ForegroundExe()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == Environment.ProcessId) return null;
            var name = Process.GetProcessById((int)pid).ProcessName;
            return Ignore.Contains(name) ? null : name;
        }
        catch { return null; }
    }

    /// <summary>Programs with a visible window (for the "save settings for this game" list).</summary>
    public static List<(string Exe, string Title)> RunningWindows()
    {
        var list = new List<(string, string)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle.Length == 0) continue;
                if (Ignore.Contains(p.ProcessName)) continue;
                list.Add((p.ProcessName, p.MainWindowTitle));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return list.DistinctBy(x => x.Item1).OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
