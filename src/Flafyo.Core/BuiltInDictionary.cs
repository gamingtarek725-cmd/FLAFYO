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

/// <summary>
/// Optional Arabic spellings for well-known brand and trademark names.
/// These entries are applied only when the user enables brand translation.
/// </summary>
public static class BrandDictionary
{
    public static Dictionary<string, string> Load() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["iRacing"] = "آي ريسنج",
            ["iRacing Arcade"] = "آي ريسنج آركيد",
            ["EA SPORTS"] = "إي إيه سبورتس",
            ["EA SPORTS FC"] = "إي إيه سبورتس إف سي",
            ["PlayStation"] = "بلايستيشن",
            ["Xbox"] = "إكس بوكس",
            ["Nintendo"] = "نينتندو",
            ["NVIDIA"] = "إنفيديا",
            ["AMD"] = "إيه إم دي",
            ["Intel"] = "إنتل",
            ["Ubisoft"] = "يوبيسوفت",
            ["Rockstar Games"] = "روكستار جيمز",
            ["Epic Games"] = "إيبيك جيمز",
            ["Steam"] = "ستيم",
            ["Logitech"] = "لوجيتك",
            ["Razer"] = "ريزر",
            ["ASUS"] = "أسوس",
            ["MSI"] = "إم إس آي",
            ["Corsair"] = "كورساير",
            ["Porsche"] = "بورشه",
            ["BMW"] = "بي إم دبليو",
            ["Mercedes-Benz"] = "مرسيدس بنز",
            ["Ferrari"] = "فيراري",
            ["Red Bull"] = "ريد بول",
            ["Monster Energy"] = "مونستر إنرجي",
        };
}
