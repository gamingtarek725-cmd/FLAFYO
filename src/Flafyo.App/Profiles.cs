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
    private const int MaxImportBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string Dir => Path.Combine(UserData.Dir, "profiles");

    private static string Safe(string exe) => new(exe.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

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
                try
                {
                    var p = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(f));
                    if (p != null && Validate(p, out _)) list.Add(p);
                }
                catch { }
            }
        }
        catch { }
        return list.OrderBy(p => p.ExeName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool Validate(GameProfile? p, out string error)
    {
        error = "";
        if (p == null) { error = "ملف الإعدادات فارغ أو غير صالح."; return false; }
        if (string.IsNullOrWhiteSpace(p.ExeName) || p.ExeName.Length > 128 ||
            p.ExeName is "." or ".." ||
            p.ExeName.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) ||
            !string.Equals(Path.GetFileName(p.ExeName), p.ExeName, StringComparison.Ordinal))
        { error = "اسم ملف اللعبة غير صالح."; return false; }
        if (p.Title == null || p.Title.Length > 256) { error = "اسم اللعبة أطول من المسموح."; return false; }
        if (p.X is < -32768 or > 32768 || p.Y is < -32768 or > 32768 ||
            p.Width is < 0 or > 32768 || p.Height is < 0 or > 32768)
        { error = "أبعاد منطقة الترجمة غير صالحة."; return false; }
        if (p.ModeIndex is < 0 or > 1 || p.CaptureIndex is < 0 or > 1 ||
            p.MonitorIndex is < 0 or > 255 || p.SpeedIndex is < 0 or > 2 ||
            p.TextSizeIndex is < 0 or > 2 || p.PackIndex is < 0 or > 1)
        { error = "إحدى قيم إعدادات اللعبة خارج النطاق المسموح."; return false; }

        var s = p.Style;
        if (s == null || string.IsNullOrWhiteSpace(s.FontFamily) || s.FontFamily.Length > 128 ||
            !double.IsFinite(s.FontSize) || s.FontSize is < 6 or > 96 ||
            s.BackOpacity is < 0 or > 100 || s.Align is < 0 or > 2 ||
            s.OverlayMode is < 0 or > 2 || s.AreaX is < -32768 or > 32768 ||
            s.AreaY is < -32768 or > 32768 || s.AreaW is < 0 or > 32768 ||
            s.AreaH is < 0 or > 32768 || s.AutoWidthPx is < 0 or > 32768 ||
            s.AutoHideSeconds is < 0 or > 3600 || s.HeadingScalePercent is < 50 or > 250 ||
            !ValidColor(s.TextColor) || !ValidColor(s.BackColor) ||
            !ValidColor(s.BorderColor) || !ValidColor(s.HeadingTextColor))
        { error = "إعدادات مظهر الترجمة تحتوي على قيم غير صالحة."; return false; }
        return true;
    }

    private static bool ValidColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    public static void Save(GameProfile p)
    {
        if (!Validate(p, out var error)) throw new InvalidDataException(error);
        Directory.CreateDirectory(Dir);
        var target = Path.Combine(Dir, Safe(p.ExeName) + ".json");
        AtomicWrite(target, JsonSerializer.Serialize(p, JsonOptions), keepBackup: File.Exists(target));
    }

    public static void Export(GameProfile p, string path)
    {
        if (!Validate(p, out var error)) throw new InvalidDataException(error);
        var json = JsonSerializer.Serialize(p, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxImportBytes)
            throw new InvalidDataException("ملف الإعدادات أكبر من الحد المسموح.");
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
    }

    public static GameProfile Import(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > MaxImportBytes)
            throw new InvalidDataException("حجم ملف الإعدادات غير صالح (الحد الأقصى 256 كيلوبايت).");
        var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        var profile = JsonSerializer.Deserialize<GameProfile>(json)
            ?? throw new InvalidDataException("تعذّر قراءة ملف الإعدادات.");
        if (!Validate(profile, out var error)) throw new InvalidDataException(error);
        return profile;
    }

    private static void AtomicWrite(string target, string content, bool keepBackup)
    {
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));
            if (keepBackup)
                File.Copy(target, target + ".bak", overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
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

    /// <summary>Programs with a visible window, with the window handle (for the "borderless fullscreen" tool).</summary>
    public static List<(string Exe, string Title, IntPtr Hwnd)> RunningWindowsEx()
    {
        var list = new List<(string, string, IntPtr)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle.Length == 0) continue;
                if (Ignore.Contains(p.ProcessName)) continue;
                list.Add((p.ProcessName, p.MainWindowTitle, p.MainWindowHandle));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return list.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
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
