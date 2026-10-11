using System.Runtime.InteropServices;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Flafyo.Windows;

public sealed record MonitorInfo(int Index, string Name, System.Drawing.Rectangle Bounds);

/// <summary>Lists the connected screens (physical pixel bounds, main screen first).</summary>
public static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc proc, IntPtr data);

    public static List<MonitorInfo> GetAll()
    {
        var rects = new List<System.Drawing.Rectangle>();
        EnumProc callback = (IntPtr m, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            rects.Add(new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        var ordered = rects.OrderBy(r => r.X == 0 && r.Y == 0 ? 0 : 1).ThenBy(r => r.X).ThenBy(r => r.Y).ToList();
        var list = new List<MonitorInfo>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var b = ordered[i];
            var main = b.X == 0 && b.Y == 0 ? " (الرئيسية)" : "";
            list.Add(new MonitorInfo(i, $"الشاشة {i + 1}{main}  {b.Width}×{b.Height}", b));
        }
        return list;
    }
}

/// <summary>Reads the Arabic translation aloud with an Arabic voice installed in Windows (works offline).</summary>
public sealed class ArabicSpeaker : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly MediaPlayer _player = new();
    private readonly VoiceInformation? _voice;

    public ArabicSpeaker()
    {
        _voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("ar", StringComparison.OrdinalIgnoreCase));
        if (_voice != null) _synth.Voice = _voice;
    }

    public bool HasArabicVoice => _voice != null;
    public string VoiceName => _voice?.DisplayName ?? "";

    public async Task SpeakAsync(string text)
    {
        if (_voice == null || string.IsNullOrWhiteSpace(text)) return;
        var stream = await _synth.SynthesizeTextToStreamAsync(text);
        _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        _player.Play();
    }

    public void Stop() => _player.Pause();

    public void Dispose() { _player.Dispose(); _synth.Dispose(); }
}
