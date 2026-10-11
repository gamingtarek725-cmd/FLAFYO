using System.Text;

namespace Flafyo.Core;

public sealed record GlossaryPack(string Id, string Title);

/// <summary>Ready-made vocabulary lists for common game types (menus, weapons, quests...). Exact-match, so they never disturb sentences.</summary>
public static class Glossaries
{
    public static readonly IReadOnlyList<GlossaryPack> All = new[]
    {
        new GlossaryPack("ui", "القوائم والإعدادات العامة"),
        new GlossaryPack("fps", "ألعاب الأكشن والتصويب"),
        new GlossaryPack("rpg", "ألعاب تقمص الأدوار"),
        new GlossaryPack("strategy", "الاستراتيجية والمحاكاة"),
        new GlossaryPack("sports", "السباقات والرياضة"),
        new GlossaryPack("survival", "البقاء والرعب"),
    };

    public static Dictionary<string, string> Load(IEnumerable<string>? ids)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ids == null) return result;
        foreach (var id in ids)
        {
            try
            {
                using var stream = typeof(Glossaries).Assembly.GetManifestResourceStream($"glossary.{id}.tsv");
                if (stream == null) continue;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0) result[parts[0].Trim()] = parts[1].Trim();
                }
            }
            catch { /* a damaged list must never stop the app */ }
        }
        return result;
    }
}
