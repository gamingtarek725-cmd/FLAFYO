using System.Text;

namespace Flafyo.Core.GameText;

/// <summary>A fixed-width replacement for a byte range discovered by BinaryTextMiner.</summary>
public sealed record TextByteReplacement(long Offset, int ByteLength, string EncodingName, string Replacement, string? ExpectedOriginal = null);

/// <summary>
/// Applies fixed-width edits to a copy of a binary file. It never changes the input array,
/// shifts offsets, or permits a replacement to grow or shrink the original byte range.
/// </summary>
public static class BinaryTextPatcher
{
    public static byte[] CreatePatchedCopy(ReadOnlySpan<byte> source, IEnumerable<TextByteReplacement> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        var edits = replacements.OrderBy(x => x.Offset).ToArray();
        if (edits.Length == 0) return source.ToArray();

        var encoded = new byte[edits.Length][];
        long previousEnd = -1;
        for (int i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            if (edit.Offset < 0 || edit.ByteLength <= 0 || edit.Offset > source.Length - (long)edit.ByteLength)
                throw new InvalidDataException("Text replacement points outside the source file.");
            if (edit.Offset < previousEnd)
                throw new InvalidDataException("Text replacements overlap.");
            previousEnd = checked(edit.Offset + edit.ByteLength);

            Encoding encoding = edit.EncodingName switch
            {
                "ASCII / UTF-8" => Encoding.GetEncoding("us-ascii",
                    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
                "UTF-16 LE" => new UnicodeEncoding(false, false, true),
                "UTF-16 BE" => new UnicodeEncoding(true, false, true),
                _ => throw new InvalidDataException("Unsupported text encoding: " + edit.EncodingName)
            };

            if (edit.ExpectedOriginal != null)
            {
                string current;
                try { current = TextNormalizer.Normalize(encoding.GetString(source.Slice(checked((int)edit.Offset), edit.ByteLength))); }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException("Original byte range is no longer valid for its detected encoding.", ex);
                }

                if (!string.Equals(current, TextNormalizer.Normalize(edit.ExpectedOriginal), StringComparison.Ordinal))
                    throw new InvalidDataException("The source text changed after scanning. Scan the file again before saving edits.");
            }

            try { encoded[i] = encoding.GetBytes(edit.Replacement ?? string.Empty); }
            catch (EncoderFallbackException ex)
            {
                throw new InvalidDataException("Replacement contains characters unsupported by the original encoding.", ex);
            }

            if (encoded[i].Length != edit.ByteLength)
                throw new InvalidDataException("Replacement must use exactly the same number of bytes as the original text.");
        }

        var result = source.ToArray();
        for (int i = 0; i < edits.Length; i++)
            encoded[i].CopyTo(result, checked((int)edits[i].Offset));
        return result;
    }
}
