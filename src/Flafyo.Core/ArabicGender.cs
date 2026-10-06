using System.Text;
using System.Text.RegularExpressions;

namespace Flafyo.Core;

/// <summary>
/// Translation models often address the listener in the FEMININE ("انتبهي", "كنت مخطئة"). This converts the most common
/// feminine forms to masculine. It is rule/list based (never guesses), and the user can extend it with masculine.txt.
/// </summary>
public static class ArabicGender
{
    private static readonly Regex Word = new(@"[\u0621-\u064A]+", RegexOptions.Compiled);

    private static Dictionary<string, string> Parse(string pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in pairs.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int i = p.IndexOf('=');
            if (i > 0) d[p[..i]] = p[(i + 1)..];
        }
        return d;
    }

    // feminine → masculine (imperatives, negative commands, 2nd-person present)
    private static readonly Dictionary<string, string> Verbs = Parse(@"
        انتبهي=انتبه اذهبي=اذهب افتحي=افتح أغلقي=أغلق اغلقي=اغلق اضغطي=اضغط ابقي=ابق خذي=خذ تعالي=تعال انظري=انظر
        استمعي=استمع توقفي=توقف اهربي=اهرب اركضي=اركض ابتعدي=ابتعد كوني=كن قولي=قل اجلسي=اجلس قفي=قف ابحثي=ابحث
        اعثري=اعثر جدي=جد اقتلي=اقتل احذري=احذر تذكري=تذكر اتبعيني=اتبعني ساعديني=ساعدني أخبريني=أخبرني اخبريني=اخبرني
        دعيني=دعني اتركيني=اتركني أعطيني=أعطني اعطيني=اعطني أريني=أرني اسمعي=اسمع ارجعي=ارجع عودي=عد اصمتي=اصمت
        اخرجي=اخرج ادخلي=ادخل اهدئي=اهدأ انتظري=انتظر ثقي=ثق حاولي=حاول اختاري=اختر ابدئي=ابدأ تحركي=تحرك أسرعي=أسرع
        استعدي=استعد اهتمي=اهتم دعي=دع عيشي=عش موتي=مت تمهلي=تمهل اصبري=اصبر اعرفي=اعرف اعلمي=اعلم افعلي=افعل
        اشربي=اشرب كلي=كل اقفزي=اقفز اركبي=اركب احملي=احمل أحضري=أحضر احضري=احضر اجمعي=اجمع اقرئي=اقرأ اكتبي=اكتب
        تأكدي=تأكد تجنبي=تجنب أنقذيني=أنقذني انقذيني=انقذني ساعدي=ساعد اسألي=اسأل أجيبي=أجب
        تقلقي=تقلق تخافي=تخف تتحركي=تتحرك تذهبي=تذهب تفعلي=تفعل تنسي=تنس تحاولي=تحاول تقتربي=تقترب تتوقفي=تتوقف
        تصدقي=تصدق تثقي=تثق تقولي=تقل تلمسي=تلمس تتركي=تترك تتحدثي=تتحدث تنظري=تنظر تفتحي=تفتح تتأخري=تتأخر
        تعرفين=تعرف تريدين=تريد تستطيعين=تستطيع تفعلين=تفعل تقولين=تقول تظنين=تظن تعتقدين=تعتقد تحبين=تحب
        تذهبين=تذهب تفهمين=تفهم تعلمين=تعلم تملكين=تملك تحتاجين=تحتاج تبحثين=تبحث تنتظرين=تنتظر تحاولين=تحاول
        تشعرين=تشعر تخافين=تخاف تقدرين=تقدر تعيشين=تعيش تتذكرين=تتذكر");

    // feminine adjective → masculine, applied only right after "أنا / كنت / أشعر ..."
    private static readonly Dictionary<string, string> Adjectives = Parse(@"
        مخطئة=مخطئاً متأكدة=متأكداً سعيدة=سعيداً متعبة=متعباً خائفة=خائفاً غاضبة=غاضباً مستعدة=مستعداً وحيدة=وحيداً
        حزينة=حزيناً آسفة=آسفاً ممتنة=ممتناً قلقة=قلقاً مرتبكة=مرتبكاً متحمسة=متحمساً فخورة=فخوراً مصدومة=مصدوماً
        محظوظة=محظوظاً جائعة=جائعاً مريضة=مريضاً مضطربة=مضطرباً منزعجة=منزعجاً مرهقة=مرهقاً مدينة=مديناً");

    private static readonly HashSet<string> Triggers = new(StringComparer.Ordinal)
    {
        "أنا", "كنت", "لست", "أصبحت", "أشعر", "أنني", "أني", "أبدو", "بدوت", "صرت", "ظللت", "اصبحت", "اشعر",
    };

    public static IReadOnlyDictionary<string, string> ParseUserFile(IEnumerable<string> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int i = line.IndexOf('=');
            if (i > 0 && i < line.Length - 1) d[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return d;
    }

    private static string? Lookup(Dictionary<string, string> map, string w)
    {
        if (map.TryGetValue(w, out var hit)) return hit;
        if (w.Length > 3 && (w[0] == 'و' || w[0] == 'ف') && map.TryGetValue(w[1..], out var rest)) return w[0] + rest;
        return null;
    }

    private static string Bare(string w) => w.Length > 3 && (w[0] == 'و' || w[0] == 'ف') ? w[1..] : w;

    public static string MakeMasculine(string text, IReadOnlyDictionary<string, string>? extra = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var matches = Word.Matches(text);
        if (matches.Count == 0) return text;

        var sb = new StringBuilder(text.Length);
        int last = 0;
        string prev1 = "", prev2 = "";
        foreach (Match m in matches)
        {
            sb.Append(text, last, m.Index - last);
            last = m.Index + m.Length;
            var w = m.Value;
            string? rep = null;

            if (extra != null && extra.TryGetValue(w, out var user)) rep = user;
            rep ??= Lookup(Verbs, w);
            if (rep == null && (Triggers.Contains(Bare(prev1)) || Triggers.Contains(Bare(prev2))))
                rep = Lookup(Adjectives, w);

            sb.Append(rep ?? w);
            prev2 = prev1; prev1 = w;
        }
        sb.Append(text, last, text.Length - last);
        return sb.ToString();
    }
}

/// <summary>Applies a post-processing function to every translation (e.g. the masculine fix).</summary>
public sealed class PostProcessTranslator : ITranslator, IDisposable
{
    private readonly ITranslator _inner;
    private readonly Func<string, string> _post;

    public PostProcessTranslator(ITranslator inner, Func<string, string> post) { _inner = inner; _post = post; }

    public async Task<string> TranslateAsync(string text, CancellationToken ct)
    {
        var r = await _inner.TranslateAsync(text, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(r) ? r : _post(r);
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();
}
