using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Flafyo.Core.GameText;

/// <summary>
/// Read-only inspection of a game/localization file. It identifies formats only when their
/// signature or existing FLAFYO parser provides evidence; unknown binary formats are never guessed.
/// </summary>
public sealed record FormatAnalysisReport(
    string FileName,
    string Extension,
    long SizeBytes,
    string Kind,
    string Signature,
    string Encoding,
    double PrintableRatio,
    int EntryCount,
    IReadOnlyList<string> Samples,
    IReadOnlyList<string> Warnings)
{
    public string Summary =>
        $"{Kind} | {SizeBytes:N0} بايت | {Signature}" +
        (EntryCount > 0 ? $" | {EntryCount:N0} عنصر" : "");

    public string Details
    {
        get
        {
            var lines = new List<string>
            {
                $"الملف: {FileName}",
                $"الامتداد: {Extension}",
                $"الحجم: {SizeBytes:N0} بايت",
                $"التصنيف: {Kind}",
                $"التوقيع: {Signature}",
                $"الترميز: {Encoding}",
                $"نسبة البيانات المقروءة كنص: {PrintableRatio:P0}",
                EntryCount > 0 ? $"عدد العناصر داخل الحاوية: {EntryCount:N0}" : "لا توجد عناصر أرشيف مفحوصة."
            };
            if (Samples.Count > 0)
            {
                lines.Add("");
                lines.Add("عينات / أسماء عناصر:");
                lines.AddRange(Samples.Select(x => "• " + x));
            }
            if (Warnings.Count > 0)
            {
                lines.Add("");
                lines.Add("ملاحظات وحدود:");
                lines.AddRange(Warnings.Select(x => "• " + x));
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}

public static class GameFileFormatAnalyzer
{
    private const long MaxInspectBytes = 256L * 1024 * 1024;
    private const int MaxArchiveEntries = 10000;
    private const long MaxListedUncompressedBytes = 512L * 1024 * 1024;

    public static FormatAnalysisReport Analyze(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("مسار الملف فارغ.", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("الملف غير موجود.", path);

        var ext = info.Extension.ToLowerInvariant();
        var warnings = new List<string>();
        if (info.Length > MaxInspectBytes)
        {
            var header = new byte[16];
            using (var stream = File.OpenRead(path)) _ = stream.Read(header, 0, header.Length);
            return new FormatAnalysisReport(info.Name, ext, info.Length,
                "ملف كبير — فحص أولي فقط", DetectSignature(header), "لم يتم فحص الترميز",
                0, 0, Array.Empty<string>(),
                new[] { "الحجم أكبر من حد الفحص الآمن (256 MiB). لم تتم قراءة الملف كاملًا أو فك ضغطه." });
        }

        var data = File.ReadAllBytes(path);
        var signature = DetectSignature(data);
        var ratio = PrintableRatio(data);

        if (ArcArchive.IsArc(data))
        {
            try
            {
                var arc = ArcArchive.Parse(data);
                return new FormatAnalysisReport(info.Name, ext, info.Length,
                    "أرشيف Capcom ARC", signature, "بيانات ثنائية مضغوطة داخل الأرشيف", ratio,
                    arc.Entries.Count,
                    arc.Entries.Take(20).Select(e => $"{e.Name} [{e.TypeName}] — {e.Size:N0} بايت").ToList(),
                    new[] { "تم فحص جدول الأرشيف فقط. لم يتم تعديل الملف أو استخراج موارده." });
            }
            catch (Exception ex)
            {
                warnings.Add("ظهر توقيع ARC لكن تعذّر التحقق من بنية الأرشيف: " + ex.Message);
                return Report("أرشيف ARC تالف أو غير متوافق", "ARC header", "غير معروف", 0, Array.Empty<string>());
            }
        }

        if (ContainerTools.Detect(data) == ContainerKind.Zip)
        {
            try
            {
                using var stream = new MemoryStream(data, writable: false);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                long total = 0;
                var samples = new List<string>();
                int count = 0;
                foreach (var entry in zip.Entries)
                {
                    if (++count > MaxArchiveEntries)
                    {
                        warnings.Add("توقف تعداد العناصر عند 10,000 عنصر كحد أمان.");
                        count = MaxArchiveEntries;
                        break;
                    }
                    total += entry.Length;
                    if (samples.Count < 20) samples.Add($"{entry.FullName} — {entry.Length:N0} بايت");
                    if (total > MaxListedUncompressedBytes)
                    {
                        warnings.Add("تجاوز الحجم غير المضغوط المعلن 512 MiB؛ لم يتم استخراج أي عنصر.");
                        break;
                    }
                }
                warnings.Add("تمت قراءة فهرس ZIP فقط؛ لم يتم استخراج الملفات أو تشغيل أي ملف بداخله.");
                return Report("حاوية ZIP (قد تستخدم امتدادًا مثل .pak)", "ZIP / PK", "غير منطبق", count, samples);
            }
            catch (Exception ex)
            {
                warnings.Add("ظهر توقيع ZIP لكن تعذّر قراءة الفهرس: " + ex.Message);
                return Report("حاوية ZIP غير صالحة أو غير مدعومة", "ZIP / PK", "غير معروف", 0, Array.Empty<string>());
            }
        }

        var format = GameFormats.For(path, includePlainText: true);
        if (format != null && ratio >= 0.55)
        {
            DecodedFile decoded;
            try { decoded = TextFileIO.Decode(data); }
            catch (Exception ex)
            {
                warnings.Add("تعذّر تحديد الترميز: " + ex.Message);
                return Report("ملف نصي مدعوم — تعذّر فك الترميز", format.Name, "غير معروف", 0, Array.Empty<string>());
            }

            var samples = decoded.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).Take(12).ToList();
            warnings.Add("الصيغة مدعومة بمحوّل نصي موجود. التحليل لا يثبت أن كل النصوص داخل الملف نصوص واجهة؛ راجع المعاينة قبل الترجمة.");
            return Report($"ملف نصي معروف — {format.Name}", format.Name, decoded.Encoding.WebName, 0, samples);
        }

        if (ratio >= 0.75)
        {
            warnings.Add("الملف يبدو نصيًا، لكن لا يوجد محوّل معروف لامتداده حتى الآن. يمكن دراسة بنيته قبل إضافة دعم كتابة.");
            return Report("ملف نصي غير معروف الصيغة", "نص غير مصنّف", "UTF-8 أو ترميز أحادي البايت محتمل", 0,
                TextFileIO.Decode(data).Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).Take(12).ToList());
        }

        warnings.Add("صيغة ثنائية غير معروفة. لا يمكن تحديد طريقة فكها أو تعديلها بأمان من الامتداد وحده؛ يلزم ملف عينة أصلي وفحص بنيوي متخصص.");
        warnings.Add("لم يتم فك الضغط أو استخراج النصوص أو تعديل الملف.");
        return Report("ملف ثنائي غير معروف — يحتاج محوّلًا متخصصًا", signature, "غير منطبق", 0, Array.Empty<string>());

        FormatAnalysisReport Report(string kind, string detected, string encoding, int count, IReadOnlyList<string> samples) =>
            new(info.Name, ext, info.Length, kind, detected, encoding, ratio, count, samples, warnings);
    }

    private static string DetectSignature(ReadOnlySpan<byte> data)
    {
        if (ArcArchive.IsArc(data)) return "ARC\\0 — Capcom MT Framework";
        if (data.Length >= 4 && data[0] == 'P' && data[1] == 'K' &&
            (data[2] == 3 || data[2] == 5 || data[2] == 7)) return "PK — ZIP container";
        if (data.StartsWith("SQLite format 3\\0"u8)) return "SQLite database";
        if (data.Length >= 4 && data[0] == 'M' && data[1] == 'Z') return "Windows PE executable";
        if (data.Length >= 4 && data[0] == 'D' && data[1] == 'D' && data[2] == 'S' && data[3] == ' ') return "DDS texture";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return "PNG image";
        if (data.Length >= 4 && data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S') return "Ogg media";
        if (data.Length >= 4 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F') return "RIFF media/container";
        if (data.Length == 0) return "Empty file";
        return "Unknown signature";
    }

    private static double PrintableRatio(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return 0;
        int printable = 0;
        foreach (byte b in data)
            if (b is 9 or 10 or 13 || b >= 32) printable++;
        return (double)printable / data.Length;
    }
}
