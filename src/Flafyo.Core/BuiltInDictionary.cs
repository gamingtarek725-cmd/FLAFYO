namespace Flafyo.Core;

public static class BuiltInDictionary
{
    /// <summary>Small gaming dictionary + optional user file (lines: english=arabic) next to the exe.</summary>
    public static Dictionary<string, string> Load(string? userFile = null)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Reload"] = "إعادة التلقيم", ["Inventory"] = "المخزون", ["Health"] = "الصحة", ["Ammo"] = "الذخيرة",
            ["Quest"] = "المهمة", ["Save"] = "حفظ", ["Load"] = "تحميل", ["Resume"] = "استئناف",
            ["Options"] = "الخيارات", ["Settings"] = "الإعدادات", ["Exit"] = "خروج", ["Pause"] = "إيقاف مؤقت",
            ["Map"] = "الخريطة", ["Skills"] = "المهارات", ["Continue"] = "متابعة", ["New Game"] = "لعبة جديدة",
        };
        try
        {
            if (userFile != null && File.Exists(userFile))
                foreach (var line in File.ReadAllLines(userFile))
                {
                    if (line.TrimStart().StartsWith('#')) continue;
                    var i = line.IndexOf('=');
                    if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim();
                }
        }
        catch { /* a broken user file must never stop the app */ }
        return d;
    }
}
