using System.IO;
using System.Text.Json;

namespace Flafyo.Desktop;

/// <summary>Per-user files in %LOCALAPPDATA%\FLAFYO (editable without admin rights, survives updates).</summary>
internal static class UserData
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLAFYO");
    public static string DictionaryFile => Path.Combine(Dir, "dictionary.txt");
    public static string IdiomsFile => Path.Combine(Dir, "idioms.txt");
    public static string MasculineFile => Path.Combine(Dir, "masculine.txt");
    public static string ErrorLog => Path.Combine(Dir, "error.log");
    public static string HistoryFile => Path.Combine(Dir, "history.txt");
    public static string SettingsFile => Path.Combine(Dir, "settings.json");

    public static void EnsureTemplates()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(DictionaryFile))
                File.WriteAllText(DictionaryFile,
                    "# قاموس الألعاب: كل سطر بالشكل  كلمة_إنجليزي = الترجمة\n# مثال:\n# Reload = إعادة التلقيم\n", System.Text.Encoding.UTF8);
            if (!File.Exists(IdiomsFile))
                File.WriteAllText(IdiomsFile,
                    "# الاصطلاحات: اكتب العبارة الإنجليزي = معناها بإنجليزي بسيط (بتتبدل قبل الترجمة)\n" +
                    "# النجمة * تعني كلمة واحدة وتتكرر في الناحية التانية. الشرطة المائلة / للبدائل.\n" +
                    "# مثال:\n# get/got round * = persuade *\n# kick the bucket = die\n", System.Text.Encoding.UTF8);
            if (!File.Exists(MasculineFile))
                File.WriteAllText(MasculineFile,
                    "# تحويل صيغة المؤنث للمذكر: المؤنث = المذكر (بتتضاف لقائمة البرنامج الجاهزة)\n# مثال:\n# اجلسي = اجلس\n", System.Text.Encoding.UTF8);
        }
        catch { /* never block the app because of files */ }
    }
}

public sealed class AppSettings
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int SpeedIndex { get; set; } = 1;
    public int TextSizeIndex { get; set; } = 1;
    public int PackIndex { get; set; } = 0;
    public bool Masculine { get; set; } = true;
    public bool TranslateBrands { get; set; } = false;
    public bool Precise { get; set; } = false;
    public OverlayStyle Style { get; set; } = new();
    public int ModeIndex { get; set; }                     // 0 = one text region, 1 = whole screen
    public int CaptureIndex { get; set; }                  // 0 = normal, 1 = advanced (DXGI) for fullscreen games
    public bool Continuous { get; set; } = true;
    public string GameFolder { get; set; } = "";
    public int MonitorIndex { get; set; }
    public bool Speak { get; set; }
    public bool AutoProfile { get; set; } = true;
    public List<string> Glossaries { get; set; } = new();
    public int AiProviderIndex { get; set; }
    public string AiApiKey { get; set; } = "";
    public string AiModel { get; set; } = "";
    public string AiBaseUrl { get; set; } = "";
    public bool UseAiApi { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(UserData.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(UserData.SettingsFile)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(UserData.Dir);
            File.WriteAllText(UserData.SettingsFile, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

/// <summary>How the translation strip looks and where it appears (saved in settings.json).</summary>
public sealed class OverlayStyle
{
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 22;
    public bool Bold { get; set; }
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackColor { get; set; } = "#101018";
    public int BackOpacity { get; set; } = 90;            // percent
    public bool Border { get; set; } = true;
    public string BorderColor { get; set; } = "#00D9A5";
    public int Align { get; set; }                         // 0 = start (right for Arabic), 1 = center, 2 = end (left)
    public bool Shadow { get; set; } = true;
    public int OverlayMode { get; set; }                   // 0 = above the text region, 1 = below it, 2 = fixed area chosen by the user
    public int AreaX { get; set; }
    public int AreaY { get; set; }
    public int AreaW { get; set; }
    public int AreaH { get; set; }
    public int AutoWidthPx { get; set; }                   // 0 = same width as the text region
    public int AutoHideSeconds { get; set; }               // 0 = stays until the text changes
    public bool ShowOriginal { get; set; }
    // whole-screen mode: titles/headings get their own look
    public string HeadingTextColor { get; set; } = "#FFC857";
    public int HeadingScalePercent { get; set; } = 130;
    public bool HeadingBold { get; set; } = true;
    public bool AutoFit { get; set; } = true;              // font size follows the size of the original text

    public void CopyFrom(OverlayStyle o)
    {
        FontFamily = o.FontFamily; FontSize = o.FontSize; Bold = o.Bold; TextColor = o.TextColor; BackColor = o.BackColor;
        BackOpacity = o.BackOpacity; Border = o.Border; BorderColor = o.BorderColor; Align = o.Align; Shadow = o.Shadow;
        OverlayMode = o.OverlayMode; AreaX = o.AreaX; AreaY = o.AreaY; AreaW = o.AreaW; AreaH = o.AreaH;
        AutoWidthPx = o.AutoWidthPx; AutoHideSeconds = o.AutoHideSeconds; ShowOriginal = o.ShowOriginal;
        HeadingTextColor = o.HeadingTextColor; HeadingScalePercent = o.HeadingScalePercent; HeadingBold = o.HeadingBold; AutoFit = o.AutoFit;
    }
}
