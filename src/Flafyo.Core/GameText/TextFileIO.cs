using System.Text;

namespace Flafyo.Core.GameText;

public sealed record DecodedFile(string Text, Encoding Encoding, bool Bom);

/// <summary>Reads game text files in whatever encoding they use, and writes them back without breaking them.</summary>
public static class TextFileIO
{
    static TextFileIO() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static DecodedFile Read(string path) => Decode(File.ReadAllBytes(path));

    public static DecodedFile Decode(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return new DecodedFile(new UTF8Encoding(true).GetString(b, 3, b.Length - 3), new UTF8Encoding(true), true);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return new DecodedFile(Encoding.Unicode.GetString(b, 2, b.Length - 2), Encoding.Unicode, true);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return new DecodedFile(Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2), Encoding.BigEndianUnicode, true);
        try { return new DecodedFile(new UTF8Encoding(false, true).GetString(b), new UTF8Encoding(false), false); }
        catch (DecoderFallbackException)
        {
            var legacy = Encoding.GetEncoding(1252);
            return new DecodedFile(legacy.GetString(b), legacy, false);
        }
    }

    /// <summary>Same encoding as the original; UTF-8 when the original code page cannot hold the new (Arabic) text.</summary>
    public static byte[] Encode(string text, DecodedFile original)
    {
        var enc = original.Encoding;
        bool unicode = enc is UnicodeEncoding or UTF8Encoding;
        bool switched = false;
        if (!unicode && text.Any(c => c > 255)) { enc = new UTF8Encoding(false); switched = true; }
        var body = enc.GetBytes(text);
        var preamble = original.Bom && !switched ? enc.GetPreamble() : Array.Empty<byte>();
        var all = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, all, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, all, preamble.Length, body.Length);
        return all;
    }
}
