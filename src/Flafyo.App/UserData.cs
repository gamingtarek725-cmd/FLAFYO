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
