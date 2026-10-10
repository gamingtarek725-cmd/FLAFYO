using System.Text;

namespace Flafyo.Core.GameText;

/// <summary>
/// Many game engines cannot display Arabic correctly (letters stay disconnected and the line runs left-to-right).
/// This joins the letters (Unicode presentation forms) and reverses the line so it looks right in such engines.
/// Only use it for games without Arabic support: engines with real Arabic support need the normal text.
/// </summary>
public static class ArabicShaper
{
    // letter → isolated, final, initial, medial (initial/medial = 0 when the letter does not join the next one)
    private static readonly Dictionary<char, int[]> Forms = Build();

    private static Dictionary<char, int[]> Build()
    {
        var d = new Dictionary<char, int[]>();
        void Add(int letter, int iso, int fin, int ini = 0, int med = 0) => d[(char)letter] = new[] { iso, fin, ini, med };
        Add(0x0621, 0xFE80, 0xFE80);
        Add(0x0622, 0xFE81, 0xFE82); Add(0x0623, 0xFE83, 0xFE84); Add(0x0624, 0xFE85, 0xFE86); Add(0x0625, 0xFE87, 0xFE88);
        Add(0x0626, 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C); Add(0x0627, 0xFE8D, 0xFE8E);
        Add(0x0628, 0xFE8F, 0xFE90, 0xFE91, 0xFE92); Add(0x0629, 0xFE93, 0xFE94);
        Add(0x062A, 0xFE95, 0xFE96, 0xFE97, 0xFE98); Add(0x062B, 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C);
        Add(0x062C, 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0); Add(0x062D, 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4);
        Add(0x062E, 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8); Add(0x062F, 0xFEA9, 0xFEAA); Add(0x0630, 0xFEAB, 0xFEAC);
        Add(0x0631, 0xFEAD, 0xFEAE); Add(0x0632, 0xFEAF, 0xFEB0);
        Add(0x0633, 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4); Add(0x0634, 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8);
        Add(0x0635, 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC); Add(0x0636, 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0);
        Add(0x0637, 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4); Add(0x0638, 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8);
        Add(0x0639, 0xFEC9, 0xFECA, 0xFECB, 0xFECC); Add(0x063A, 0xFECD, 0xFECE, 0xFECF, 0xFED0);
        Add(0x0641, 0xFED1, 0xFED2, 0xFED3, 0xFED4); Add(0x0642, 0xFED5, 0xFED6, 0xFED7, 0xFED8);
        Add(0x0643, 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC); Add(0x0644, 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0);
        Add(0x0645, 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4); Add(0x0646, 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8);
        Add(0x0647, 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC); Add(0x0648, 0xFEED, 0xFEEE); Add(0x0649, 0xFEEF, 0xFEF0);
        Add(0x064A, 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4);
        return d;
    }

    // lam + alef ligatures: alef → (isolated, final)
    private static readonly Dictionary<char, (char Iso, char Fin)> Ligatures = new()
    {
        ['\u0627'] = ('\uFEFB', '\uFEFC'), ['\u0623'] = ('\uFEF7', '\uFEF8'),
        ['\u0625'] = ('\uFEF9', '\uFEFA'), ['\u0622'] = ('\uFEF5', '\uFEF6'),
    };

    private static bool IsDiacritic(char c) => c >= '\u064B' && c <= '\u0652';

    private static int Prev(char[] a, int i) { for (int k = i - 1; k >= 0; k--) if (!IsDiacritic(a[k])) return k; return -1; }
    private static int Next(char[] a, int i) { for (int k = i + 1; k < a.Length; k++) if (!IsDiacritic(a[k])) return k; return -1; }

    /// <summary>Connected letter forms, still in logical order.</summary>
    public static string Shape(string text)
    {
        var a = text.ToCharArray();
        var sb = new StringBuilder(a.Length);
        for (int i = 0; i < a.Length; i++)
        {
            char c = a[i];
            if (!Forms.TryGetValue(c, out var f)) { sb.Append(c); continue; }

            int p = Prev(a, i), n = Next(a, i);
            bool prevJoins = p >= 0 && Forms.TryGetValue(a[p], out var pf) && pf[2] != 0;

            if (c == '\u0644' && n >= 0 && Ligatures.TryGetValue(a[n], out var lig))
            {
                sb.Append(prevJoins ? lig.Fin : lig.Iso);
                i = n;
                continue;
            }

            bool nextIsLetter = n >= 0 && Forms.ContainsKey(a[n]);
            bool joinNext = f[2] != 0 && nextIsLetter;
            int form = prevJoins ? (joinNext ? f[3] : f[1]) : (joinNext ? f[2] : f[0]);
            sb.Append((char)form);
        }
        return sb.ToString();
    }

    private static bool IsLtr(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c >= 0xE000 && c <= 0xF8FF);

    private static char Mirror(char c) => c switch
    {
        '(' => ')', ')' => '(', '[' => ']', ']' => '[', '{' => '}', '}' => '{', '<' => '>', '>' => '<', '«' => '»', '»' => '«', _ => c,
    };

    /// <summary>Joined letters in visual (left-to-right) order, for engines that print every line left-to-right.</summary>
    public static string ToVisual(string line)
    {
        var a = Shape(line).ToCharArray();
        Array.Reverse(a);
        for (int i = 0; i < a.Length;)
        {
            if (IsLtr(a[i]))
            {
                int j = i;
                while (j < a.Length && IsLtr(a[j])) j++;
                Array.Reverse(a, i, j - i);          // numbers and Latin words keep reading left-to-right
                i = j;
            }
            else i++;
        }
        for (int i = 0; i < a.Length; i++) a[i] = Mirror(a[i]);
        return new string(a);
    }

    /// <summary>Whole string: every line is shaped on its own; codes like {0} or %s stay exactly as they are.</summary>
    public static string ToVisualProtected(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var part in PlaceholderGuard.NewLine.Split(text))
        {
            var m = PlaceholderGuard.NewLine.Match(part);
            if (m.Success && m.Length == part.Length) { sb.Append(part); continue; }
            var atoms = PlaceholderGuard.ToAtoms(part, out var tokens);
            sb.Append(PlaceholderGuard.FromAtoms(ToVisual(atoms), tokens));
        }
        return sb.ToString();
    }
}
