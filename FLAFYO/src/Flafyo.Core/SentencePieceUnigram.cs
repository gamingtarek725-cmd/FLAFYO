using System.Text;

namespace Flafyo.Core;

/// <summary>Minimal SentencePiece *unigram* segmenter (Viterbi) that reads a .spm model file directly.</summary>
public sealed class SentencePieceUnigram
{
    private readonly Dictionary<string, float> _scores = new(StringComparer.Ordinal);
    private readonly int _maxLen;
    private readonly float _unkScore;
    public const string Unk = "<unk>";

    public SentencePieceUnigram(IEnumerable<(string Piece, float Score)> pieces)
    {
        float min = 0;
        foreach (var (p, s) in pieces)
        {
            if (p.Length == 0) continue;
            _scores[p] = s;
            if (p.Length > _maxLen) _maxLen = p.Length;
            if (s < min) min = s;
        }
        _unkScore = min - 10f;
    }

    public static SentencePieceUnigram FromModelBytes(byte[] data)
    {
        var list = new List<(string, float)>();
        int pos = 0;
        while (pos < data.Length)
        {
            ulong tag = ReadVarint(data, ref pos);
            int field = (int)(tag >> 3), wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                int len = (int)ReadVarint(data, ref pos);
                ParsePiece(data, pos, pos + len, list);
                pos += len;
            }
            else Skip(data, ref pos, wire);
        }
        return new SentencePieceUnigram(list);
    }

    private static void ParsePiece(byte[] d, int pos, int end, List<(string, float)> list)
    {
        string piece = ""; float score = 0; int type = 1;
        while (pos < end)
        {
            ulong tag = ReadVarint(d, ref pos);
            int field = (int)(tag >> 3), wire = (int)(tag & 7);
            if (field == 1 && wire == 2) { int l = (int)ReadVarint(d, ref pos); piece = Encoding.UTF8.GetString(d, pos, l); pos += l; }
            else if (field == 2 && wire == 5) { score = BitConverter.ToSingle(d, pos); pos += 4; }
            else if (field == 3 && wire == 0) type = (int)ReadVarint(d, ref pos);
            else Skip(d, ref pos, wire);
        }
        if (type == 1 || type == 4) list.Add((piece, score)); // NORMAL or USER_DEFINED only
    }

    private static ulong ReadVarint(byte[] d, ref int pos)
    {
        ulong result = 0; int shift = 0;
        while (true)
        {
            byte b = d[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
    }

    private static void Skip(byte[] d, ref int pos, int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(d, ref pos); break;
            case 1: pos += 8; break;
            case 2: { int l = (int)ReadVarint(d, ref pos); pos += l; break; }
            case 5: pos += 4; break;
            default: throw new InvalidDataException("Unsupported protobuf wire type " + wire);
        }
    }

    /// <summary>Best segmentation of text that already contains ▁ word markers. Unknown characters become one "&lt;unk&gt;".</summary>
    public List<string> Segment(string s)
    {
        int n = s.Length;
        var result = new List<string>();
        if (n == 0) return result;
        var best = new float[n + 1];
        var back = new int[n + 1];
        var isUnk = new bool[n + 1];
        Array.Fill(best, float.NegativeInfinity);
        best[0] = 0;
        for (int i = 0; i < n; i++)
        {
            if (float.IsNegativeInfinity(best[i])) continue;
            bool any = false;
            for (int len = 1; len <= _maxLen && i + len <= n; len++)
            {
                if (_scores.TryGetValue(s.Substring(i, len), out var sc) && best[i] + sc > best[i + len])
                {
                    best[i + len] = best[i] + sc; back[i + len] = i; isUnk[i + len] = false; any = true;
                }
            }
            if (!any || float.IsNegativeInfinity(best[i + 1]))
            {
                float u = best[i] + _unkScore;
                if (u > best[i + 1]) { best[i + 1] = u; back[i + 1] = i; isUnk[i + 1] = true; }
            }
        }
        var rev = new List<string>();
        for (int end = n; end > 0; end = back[end])
            rev.Add(isUnk[end] ? Unk : s.Substring(back[end], end - back[end]));
        rev.Reverse();
        foreach (var p in rev)
            if (!(p == Unk && result.Count > 0 && result[^1] == Unk)) result.Add(p); // merge consecutive unknowns
        return result;
    }
}
