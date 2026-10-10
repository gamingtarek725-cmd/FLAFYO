using System.Net;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Flafyo.Core.GameText;

/// <summary>A text file format found in games. Only the human text is touched; everything else stays byte-for-byte the same.</summary>
public interface IGameTextFormat
{
    string Name { get; }
    bool Handles(string extension);
    /// <summary>Calls <paramref name="map"/> for every translatable string; a non-null result replaces it. Returns the new content.</summary>
    string Transform(string content, Func<string, string?> map);
}

public static class GameFormats
{
    public static IReadOnlyList<IGameTextFormat> All(bool includePlainText)
    {
        var list = new List<IGameTextFormat>
        {
            new JsonTextFormat(), new PoTextFormat(), new KeyValueTextFormat(), new SrtTextFormat(), new XmlTextFormat(), new CsvTextFormat(),
        };
        if (includePlainText) list.Add(new PlainTextFormat());
        return list;
    }

    public static IGameTextFormat? For(string path, bool includePlainText)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return All(includePlainText).FirstOrDefault(f => f.Handles(ext));
    }
}

// ---------------------------------------------------------------- JSON
public sealed class JsonTextFormat : IGameTextFormat
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string Name => "JSON";
    public bool Handles(string ext) => ext == ".json";

    public string Transform(string content, Func<string, string?> map)
    {
        var sb = new StringBuilder(content.Length + 64);
        int i = 0, n = content.Length;
        while (i < n)
        {
            char c = content[i];
            if (c == '"')
            {
                int start = i; i++;
                var raw = new StringBuilder();
                while (i < n && content[i] != '"')
                {
                    if (content[i] == '\\' && i + 1 < n) { raw.Append(content[i]).Append(content[i + 1]); i += 2; }
                    else { raw.Append(content[i]); i++; }
                }
                bool closed = i < n;
                if (closed) i++;
                int j = i;
                while (j < n && char.IsWhiteSpace(content[j])) j++;
                bool isKey = j < n && content[j] == ':';
                if (closed && !isKey)
                {
                    var text = Unescape(raw.ToString());
                    if (GameStringFilter.LooksTranslatable(text))
                    {
                        var rep = map(text);
                        if (rep != null) { sb.Append('"').Append(Escape(rep)).Append('"'); continue; }
                    }
                }
                sb.Append(content, start, i - start);
            }
            else if (c == '/' && i + 1 < n && content[i + 1] == '/')
            {
                int e = content.IndexOf('\n', i); if (e < 0) e = n;
                sb.Append(content, i, e - i); i = e;
            }
            else if (c == '/' && i + 1 < n && content[i + 1] == '*')
            {
                int e = content.IndexOf("*/", i + 2, StringComparison.Ordinal); e = e < 0 ? n : e + 2;
                sb.Append(content, i, e - i); i = e;
            }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    private static string Unescape(string literal)
    {
        try { return JsonSerializer.Deserialize<string>("\"" + literal + "\"") ?? literal; } catch { return literal; }
    }

    private static string Escape(string text)
    {
        var s = JsonSerializer.Serialize(text, Options);
        return s.Substring(1, s.Length - 2);
    }
}

// ---------------------------------------------------------------- gettext .po
public sealed class PoTextFormat : IGameTextFormat
{
    public string Name => "PO (gettext)";
    public bool Handles(string ext) => ext == ".po";

    public string Transform(string content, Func<string, string?> map)
    {
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length);
        string msgid = "", plural = "";
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.StartsWith("msgid_plural", StringComparison.Ordinal))
            {
                var (v, next) = Read(lines, i); plural = v; Copy(lines, output, i, next); i = next; continue;
            }
            if (line.StartsWith("msgid", StringComparison.Ordinal))
            {
                var (v, next) = Read(lines, i); msgid = v; plural = ""; Copy(lines, output, i, next); i = next; continue;
            }
            if (line.StartsWith("msgstr", StringComparison.Ordinal))
            {
                var (v, next) = Read(lines, i);
                if (v.Length == 0 && msgid.Length > 0)
                {
                    int idx = 0;
                    if (line.StartsWith("msgstr[", StringComparison.Ordinal))
                        int.TryParse(line.AsSpan(7, Math.Max(0, line.IndexOf(']') - 7)), out idx);
                    var source = idx > 0 && plural.Length > 0 ? plural : msgid;
                    var rep = GameStringFilter.LooksTranslatable(source) ? map(source) : null;
                    if (rep != null)
                    {
                        output.Add(line[..line.IndexOf('"')] + Quote(rep));
                        i = next; continue;
                    }
                }
                Copy(lines, output, i, next); i = next; continue;
            }
            output.Add(line); i++;
        }
        return string.Join(nl, output);
    }

    private static void Copy(string[] lines, List<string> output, int from, int to)
    {
        for (int k = from; k < to; k++) output.Add(lines[k]);
    }

    private static (string Value, int Next) Read(string[] lines, int i)
    {
        var sb = new StringBuilder();
        int q = lines[i].IndexOf('"');
        if (q >= 0) sb.Append(Unquote(lines[i][q..]));
        int j = i + 1;
        while (j < lines.Length && lines[j].TrimStart().StartsWith('"')) { sb.Append(Unquote(lines[j].Trim())); j++; }
        return (sb.ToString(), j);
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        var sb = new StringBuilder();
        for (int k = 0; k < s.Length; k++)
        {
            if (s[k] == '\\' && k + 1 < s.Length)
            {
                k++;
                sb.Append(s[k] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => s[k] });
            }
            else sb.Append(s[k]);
        }
        return sb.ToString();
    }

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
}

// ---------------------------------------------------------------- key=value files
public sealed class KeyValueTextFormat : IGameTextFormat
{
    public string Name => "Key = value";
    public bool Handles(string ext) => ext is ".properties" or ".ini" or ".lang" or ".cfg" or ".strings";

    public string Transform(string content, Func<string, string?> map)
    {
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (int k = 0; k < lines.Length; k++)
        {
            var line = lines[k];
            var t = line.TrimStart();
            if (t.Length == 0 || t[0] is '#' or ';' or '!' or '[' || t.StartsWith("//", StringComparison.Ordinal)) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var raw = line[(eq + 1)..];
            string lead = raw[..(raw.Length - raw.TrimStart().Length)];
            string trail = raw[raw.TrimEnd().Length..];
            var value = raw.Trim();
            string tail = "";
            if (value.EndsWith(';')) { tail = ";"; value = value[..^1].TrimEnd(); }
            bool quoted = value.Length >= 2 && value[0] == '"' && value[^1] == '"';
            var inner = quoted ? value[1..^1] : value;
            if (!GameStringFilter.LooksTranslatable(inner)) continue;

            var rep = map(inner);
            if (rep == null) continue;
            lines[k] = line[..(eq + 1)] + lead + (quoted ? "\"" + rep.Replace("\"", "\\\"") + "\"" : rep) + tail + trail;
        }
        return string.Join(nl, lines);
    }
}

// ---------------------------------------------------------------- subtitles
public sealed class SrtTextFormat : IGameTextFormat
{
    public string Name => "SRT (subtitles)";
    public bool Handles(string ext) => ext == ".srt";

    public string Transform(string content, Func<string, string?> map)
    {
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var output = new List<string>(lines.Length);
        int i = 0;
        while (i < lines.Length)
        {
            if (!lines[i].Contains("-->")) { output.Add(lines[i]); i++; continue; }
            output.Add(lines[i]); i++;
            var texts = new List<string>();
            while (i < lines.Length && lines[i].Trim().Length > 0) { texts.Add(lines[i]); i++; }
            var joined = string.Join(" ", texts.Select(t => t.Trim()));
            var rep = GameStringFilter.LooksTranslatable(joined) ? map(joined) : null;
            if (rep == null) output.AddRange(texts); else output.AddRange(WrapTwoLines(rep));
        }
        return string.Join(nl, output);
    }

    private static IEnumerable<string> WrapTwoLines(string s)
    {
        if (s.Length <= 42 || !s.Contains(' ')) return new[] { s };
        int mid = s.Length / 2, best = -1;
        for (int d = 0; d < s.Length; d++)
        {
            if (mid - d >= 0 && s[mid - d] == ' ') { best = mid - d; break; }
            if (mid + d < s.Length && s[mid + d] == ' ') { best = mid + d; break; }
        }
        return best <= 0 ? new[] { s } : new[] { s[..best], s[(best + 1)..] };
    }
}

// ---------------------------------------------------------------- XML
public sealed class XmlTextFormat : IGameTextFormat
{
    private static readonly Regex Pattern = new(
        @"(?<cdata><!\[CDATA\[(?<c>.*?)\]\]>)|(?<attr>\b(?<an>text|title|description|desc|caption|label|tooltip|message)=""(?<av>[^""]*)"")|(?<node>>(?<t>[^<>]+)<)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public string Name => "XML";
    public bool Handles(string ext) => ext == ".xml";

    public string Transform(string content, Func<string, string?> map) =>
        Pattern.Replace(content, m =>
        {
            if (m.Groups["cdata"].Success)
            {
                var text = m.Groups["c"].Value;
                if (!GameStringFilter.LooksTranslatable(text)) return m.Value;
                var rep = map(text);
                return rep == null || rep.Contains("]]>") ? m.Value : "<![CDATA[" + rep + "]]>";
            }
            if (m.Groups["attr"].Success)
            {
                var value = WebUtility.HtmlDecode(m.Groups["av"].Value);
                if (!GameStringFilter.LooksTranslatable(value)) return m.Value;
                var rep = map(value);
                return rep == null ? m.Value : m.Groups["an"].Value + "=\"" + SecurityElement.Escape(rep) + "\"";
            }
            var node = WebUtility.HtmlDecode(m.Groups["t"].Value);
            if (!GameStringFilter.LooksTranslatable(node)) return m.Value;
            string lead = node[..(node.Length - node.TrimStart().Length)], trail = node[node.TrimEnd().Length..];
            var r = map(node.Trim());
            return r == null ? m.Value : ">" + lead + SecurityElement.Escape(r) + trail + "<";
        });
}

// ---------------------------------------------------------------- CSV / TSV tables
public sealed class CsvTextFormat : IGameTextFormat
{
    private sealed record Cell(string Raw, string Value, bool Quoted);
    private static readonly HashSet<string> TextHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "en", "eng", "english", "en-us", "en_us", "text", "value", "string", "message", "content", "dialogue", "dialog", "description", "desc" };

    public string Name => "CSV / TSV";
    public bool Handles(string ext) => ext is ".csv" or ".tsv";

    public string Transform(string content, Func<string, string?> map)
    {
        if (content.Length == 0) return content;
        char delim = Detect(content);
        var (rows, terms) = Parse(content, delim);
        if (rows.Count < 2) return content;

        var header = rows[0].Select(c => c.Value.Trim()).ToList();
        var targets = Enumerable.Range(0, header.Count).Where(c => TextHeaders.Contains(header[c])).ToHashSet();
        if (targets.Count == 0)
        {
            bool firstIsKey = rows.Skip(1).Count(r => r.Count > 0 && !r[0].Value.Contains(' ')) >= (rows.Count - 1) * 0.7;
            for (int c = firstIsKey && header.Count > 1 ? 1 : 0; c < header.Count; c++) targets.Add(c);
        }

        var sb = new StringBuilder(content.Length + 64);
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0) sb.Append(delim);
                var cell = row[c];
                string? rep = null;
                if (r > 0 && targets.Contains(c) && GameStringFilter.LooksTranslatable(cell.Value)) rep = map(cell.Value);
                if (rep == null) { sb.Append(cell.Raw); continue; }
                bool needQuote = cell.Quoted || rep.IndexOfAny(new[] { delim, '"', '\n', '\r' }) >= 0;
                sb.Append(needQuote ? "\"" + rep.Replace("\"", "\"\"") + "\"" : rep);
            }
            sb.Append(terms[r]);
        }
        return sb.ToString();
    }

    private static char Detect(string content)
    {
        int end = content.IndexOfAny(new[] { '\r', '\n' }); if (end < 0) end = content.Length;
        var first = content[..end];
        int tabs = first.Count(c => c == '\t'), semis = first.Count(c => c == ';'), commas = first.Count(c => c == ',');
        return tabs >= semis && tabs >= commas && tabs > 0 ? '\t' : semis > commas ? ';' : ',';
    }

    private static (List<List<Cell>> Rows, List<string> Terms) Parse(string s, char delim)
    {
        var rows = new List<List<Cell>>(); var terms = new List<string>();
        var cells = new List<Cell>();
        var raw = new StringBuilder(); var val = new StringBuilder();
        bool inQuotes = false, quoted = false;
        void EndCell() { cells.Add(new Cell(raw.ToString(), val.ToString(), quoted)); raw.Clear(); val.Clear(); quoted = false; }

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inQuotes)
            {
                raw.Append(c);
                if (c == '"')
                {
                    if (i + 1 < s.Length && s[i + 1] == '"') { raw.Append('"'); val.Append('"'); i++; }
                    else inQuotes = false;
                }
                else val.Append(c);
            }
            else if (c == '"' && raw.Length == 0) { inQuotes = true; quoted = true; raw.Append(c); }
            else if (c == delim) EndCell();
            else if (c == '\r' || c == '\n')
            {
                string term = c.ToString();
                if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') { term = "\r\n"; i++; }
                EndCell(); rows.Add(cells); terms.Add(term); cells = new List<Cell>();
            }
            else { raw.Append(c); val.Append(c); }
        }
        if (raw.Length > 0 || cells.Count > 0) { EndCell(); rows.Add(cells); terms.Add(""); }
        return (rows, terms);
    }
}

// ---------------------------------------------------------------- plain text (opt-in)
public sealed class PlainTextFormat : IGameTextFormat
{
    public string Name => "Plain text";
    public bool Handles(string ext) => ext == ".txt";

    public string Transform(string content, Func<string, string?> map)
    {
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (int k = 0; k < lines.Length; k++)
        {
            var line = lines[k];
            var t = line.Trim();
            if (t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2 || !GameStringFilter.LooksTranslatable(t)) continue;
            var rep = map(t);
            if (rep == null) continue;
            lines[k] = line[..(line.Length - line.TrimStart().Length)] + rep + line[line.TrimEnd().Length..];
        }
        return string.Join(nl, lines);
    }
}
