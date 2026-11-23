using System.Text;

namespace Flafyo.Core;

/// <summary>SentencePiece tokenizer for MADLAD-400. The target language token is prepended to the source text.</summary>
public sealed class MadladTokenizer
{
    private readonly SentencePieceUnigram _segmenter;
    private readonly Dictionary<string, int> _pieceToId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _idToPiece = new();
    private readonly int _unk, _pad, _eos;

    private MadladTokenizer(SentencePieceUnigram segmenter, int unk, int pad, int eos)
    { _segmenter = segmenter; _unk = unk; _pad = pad; _eos = eos; }

    public static MadladTokenizer Load(string path) => FromModelBytes(File.ReadAllBytes(path));

    public static MadladTokenizer FromModelBytes(byte[] data)
    {
        var all = new List<(string Piece, float Score, int Type)>();
        int pos = 0;
        while (pos < data.Length)
        {
            ulong tag = ReadVarint(data, ref pos);
            int field = (int)(tag >> 3), wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                int len = checked((int)ReadVarint(data, ref pos));
                int end = checked(pos + len);
                if (end > data.Length) throw new InvalidDataException("Invalid SentencePiece model.");
                all.Add(ParsePiece(data, pos, end));
                pos = end;
            }
            else Skip(data, ref pos, wire);
        }

        if (all.Count < 3) throw new InvalidDataException("SentencePiece vocabulary is incomplete.");
        if (all[0].Piece != "<unk>" || all[1].Piece != "<s>" || all[2].Piece != "</s>")
            throw new InvalidDataException("MADLAD SentencePiece special-token IDs do not match the expected 0/1/2 contract.");

        var segmentable = all.Where(p => p.Type is 1 or 4).Select(p => (p.Piece, p.Score));
        var result = new MadladTokenizer(new SentencePieceUnigram(segmentable), 0, 1, 2);
        for (int i = 0; i < all.Count; i++)
        {
            result._pieceToId[all[i].Piece] = i;
            result._idToPiece[i] = all[i].Piece;
        }
        return result;
    }

    private static (string Piece, float Score, int Type) ParsePiece(byte[] data, int pos, int end)
    {
        string piece = "";
        float score = 0;
        int type = 1;
        while (pos < end)
        {
            ulong tag = ReadVarint(data, ref pos);
            int field = (int)(tag >> 3), wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                int len = checked((int)ReadVarint(data, ref pos));
                if (pos + len > end) throw new InvalidDataException("Invalid SentencePiece token.");
                piece = Encoding.UTF8.GetString(data, pos, len);
                pos += len;
            }
            else if (field == 2 && wire == 5)
            {
                if (pos + 4 > end) throw new InvalidDataException("Invalid SentencePiece score.");
                score = BitConverter.ToSingle(data, pos);
                pos += 4;
            }
            else if (field == 3 && wire == 0) type = checked((int)ReadVarint(data, ref pos));
            else Skip(data, ref pos, wire);
        }
        return (piece, score, type);
    }

    private static ulong ReadVarint(byte[] data, ref int pos)
    {
        ulong value = 0;
        int shift = 0;
        while (pos < data.Length && shift < 64)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
        throw new InvalidDataException("Invalid SentencePiece protobuf varint.");
    }

    private static void Skip(byte[] data, ref int pos, int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(data, ref pos); break;
            case 1: pos = checked(pos + 8); break;
            case 2: { int length = checked((int)ReadVarint(data, ref pos)); pos = checked(pos + length); break; }
            case 5: pos = checked(pos + 4); break;
            default: throw new InvalidDataException("Unsupported SentencePiece protobuf wire type " + wire);
        }
        if (pos > data.Length) throw new InvalidDataException("Invalid SentencePiece protobuf length.");
    }

    public int LanguageTokenId(string languageCode)
    {
        var token = "<2" + languageCode + ">";
        return _pieceToId.TryGetValue(token, out var id)
            ? id
            : throw new InvalidDataException("MADLAD language token is missing: " + token);
    }

    public int[] Encode(string text, string targetLanguage = "ar")
    {
        var ids = new List<int> { LanguageTokenId(targetLanguage) };
        foreach (var piece in _segmenter.Segment(MarianTokenizer.Normalize(text)))
            ids.Add(_pieceToId.TryGetValue(piece, out var id) ? id : _unk);
        ids.Add(_eos);
        return ids.ToArray();
    }

    public string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == _eos || id == _pad || id == _unk) continue;
            if (!_idToPiece.TryGetValue(id, out var piece)) continue;
            if (piece.StartsWith("<2", StringComparison.Ordinal) && piece.EndsWith('>')) continue;
            sb.Append(piece);
        }
        return sb.ToString().Replace('▁', ' ').Trim();
    }
}
