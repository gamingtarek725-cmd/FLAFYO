using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Flafyo.Core;
using Flafyo.Core.GameText;

namespace Flafyo.Desktop;

/// <summary>Scans a game folder, translates its text files with the AI model, and optionally installs them (with backup).</summary>
public partial class GameFilesWindow : Window
{
    public sealed class FileItem
    {
        public GameFileInfo Info { get; }
        public bool Suggested { get; }
        public bool IsSelected { get; set; }
        public string Text => $"{(Suggested ? "★ " : "")}{Info.RelativePath}   —   {Info.StringCount} نص";
        public FileItem(GameFileInfo info) { Info = info; Suggested = GameFileHeuristics.LooksLikeLocalization(info.RelativePath); IsSelected = Suggested; }
    }

    private readonly AppSettings _settings;
    private readonly Func<Action<string>, (TranslationResolver Resolver, ITranslator Translator)> _factory;
    private readonly ObservableCollection<FileItem> _items = new();
    private CancellationTokenSource? _cts;
    private bool _busy;

    public GameFilesWindow(AppSettings settings, Func<Action<string>, (TranslationResolver Resolver, ITranslator Translator)> factory)
    {
        InitializeComponent();
        _settings = settings; _factory = factory;
        FilesList.ItemsSource = _items;
        FilesList.SelectionChanged += (_, _) => ShowPreview();
        ProbeEditList.ItemsSource = _probeTexts;
        if (!string.IsNullOrEmpty(settings.GameFolder) && Directory.Exists(settings.GameFolder)) SetFolder(settings.GameFolder);
        StatusText.Text = "ابدأ باختيار مجلد اللعبة.";
    }

    private void Log(string s) => Dispatcher.InvokeAsync(() => { LogBox.AppendText(s + Environment.NewLine); LogBox.ScrollToEnd(); });

    private static string DefaultOutput(string gameFolder)
    {
        var name = new string(new DirectoryInfo(gameFolder).Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FLAFYO", "translated", name);
    }

    private void SetFolder(string folder)
    {
        FolderBox.Text = folder;
        OutputBox.Text = DefaultOutput(folder);
        _settings.GameFolder = folder; _settings.Save();
        _items.Clear(); CountText.Text = ""; PreviewBox.Text = "";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "اختر مجلد اللعبة" };
        if (dialog.ShowDialog(this) == true) SetFolder(dialog.FolderName);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ScanButton.IsEnabled = StartButton.IsEnabled = ApplyButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
    }

    private void ShowPreview()
    {
        if (FilesList.SelectedItem is not FileItem item) return;
        try
        {
            var sample = GameFileHeuristics.Sample(item.Info.FullPath, 12);
            PreviewBox.Text = sample.Count == 0 ? "(مفيش نصوص)" : string.Join(Environment.NewLine, sample.Select(s => "• " + s));
        }
        catch (Exception ex) { PreviewBox.Text = "تعذّر قراءة الملف: " + ex.Message; }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var folder = FolderBox.Text;
        if (!Directory.Exists(folder)) { StatusText.Text = "اختر مجلد اللعبة الأول."; return; }
        SetBusy(true); StopButton.IsEnabled = false;
        _items.Clear(); PreviewBox.Text = ""; StatusText.Text = "جاري فحص الملفات…";
        try
        {
            var options = new GameScanOptions { IncludePlainText = PlainBox.IsChecked == true };
            var files = await Task.Run(() => GameScanner.Scan(folder, options, CancellationToken.None, Log));
            foreach (var f in files) _items.Add(new FileItem(f));
            int strings = files.Sum(f => f.StringCount);
            int suggested = _items.Count(i => i.Suggested);
            CountText.Text = $"لقيت {files.Count} ملف فيهم {strings} نص. الملفات اللي عليها ★ شكلها ملفات نصوص اللعبة ({suggested} ملف)، وهي المتحددة تلقائي.";
            StatusText.Text = files.Count == 0
                ? "مفيش ملفات نصية مفهومة. الألعاب كتير بتحط النصوص في ملفات مضغوطة أو مشفرة، والبرنامج مش بيفكها."
                : suggested == 0
                    ? "مفيش ملف واضح إنه نصوص اللعبة. دوس على كل ملف وشوف النماذج تحت، وحدد اللي فيه كلام فعلاً."
                    : "راجع الملفات المحددة (دوس على أي ملف تشوف نماذج منه)، وبعدها ابدأ بالاختبار السريع.";
        }
        catch (Exception ex) { StatusText.Text = "فشل الفحص: " + ex.Message; AppLog.Write(ex); }
        finally { SetBusy(false); FilesList.Items.Refresh(); }
    }


    // Read-only format analysis: never extracts, executes, or modifies the selected file.
    private void AnalyzeFormat_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "اختر ملف التعريب أو ملفًا من ملفات اللعبة لتحليل صيغته",
            Filter = "كل الملفات (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (Directory.Exists(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var report = GameFileFormatAnalyzer.Analyze(dialog.FileName);
            ProbeStatus.Text = report.Summary;
            ProbePreview.Text = report.Details;
            Log("تحليل صيغة (قراءة فقط): " + dialog.FileName);
        }
        catch (Exception ex)
        {
            ProbeStatus.Text = "تعذّر تحليل الملف: " + ex.Message;
            ProbePreview.Text = "";
            AppLog.Write(ex);
        }
    }

    // ---- smart probe: which files really hold the game's text ----
    public sealed class ProbeItem
    {
        public ProbeHit Hit { get; }
        public string Text => $"{Hit.Sentences} جملة — [{Hit.Kind}] {Hit.Title}";
        public ProbeItem(ProbeHit hit) => Hit = hit;
    }

    public sealed class ProbeEditItem
    {
        public FoundText Found { get; }
        public string DisplayText => $"[{Found.Offset:X8} | {Found.EncodingName} | {Found.ByteLength} بايت] {Found.Text}";
        public ProbeEditItem(FoundText found) => Found = found;
    }

    private readonly ObservableCollection<ProbeEditItem> _probeTexts = new();
    private readonly Dictionary<long, TextByteReplacement> _pendingProbeEdits = new();
    private ProbeHit? _selectedEditableHit;

    private readonly ObservableCollection<ProbeItem> _probe = new();
    private CancellationTokenSource? _probeCts;

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text;
        if (!Directory.Exists(folder)) { ProbeStatus.Text = "اختر مجلد اللعبة الأول."; return; }
        ProbeList.ItemsSource = _probe;
        if (_probeCts != null) return;
        _probe.Clear(); ProbePreview.Text = "";
        ProbeButton.IsEnabled = false;
        CancelProbeButton.IsEnabled = true;
        ProbeStatus.Text = "جاري الفحص… ممكن ياخد دقايق لو اللعبة كبيرة.";
        _probeCts = new CancellationTokenSource();
        try
        {
            var token = _probeCts.Token;
            var hits = await Task.Run(() => GameProbe.Scan(folder, token, m => Dispatcher.InvokeAsync(() => ProbeStatus.Text = m)));
            foreach (var h in hits) _probe.Add(new ProbeItem(h));
            ProbeStatus.Text = hits.Count == 0
                ? "ما لقيتش نصوص إنجليزي واضحة. غالبًا النصوص في صيغة مشفرة أو مضغوطة بطريقة تانية."
                : $"لقيت {hits.Count} ملف أو جزء فيه نصوص. الأعلى في القايمة هو الأرجح إنه نصوص اللعبة.";
        }
        catch (OperationCanceledException) { ProbeStatus.Text = "اتوقف الفحص."; }
        catch (Exception ex) { ProbeStatus.Text = "فشل الفحص: " + ex.Message; AppLog.Write(ex); }
        finally
        {
            ProbeButton.IsEnabled = true;
            CancelProbeButton.IsEnabled = false;
            _probeCts?.Dispose();
            _probeCts = null;
        }
    }

    private void CancelProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_probeCts == null || _probeCts.IsCancellationRequested) return;
        CancelProbeButton.IsEnabled = false;
        ProbeStatus.Text = "جاري إيقاف الفحص…";
        _probeCts.Cancel();
    }

    private void ProbeList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProbeList.SelectedItem is ProbeItem it)
        {
            var preview = it.Hit.All
                .Where(s => BinaryTextMiner.IsRealSentence(s.Text))
                .Take(24)
                .Select(s => $"[{s.Offset:X8} | {s.EncodingName}] {s.Text}");
            ProbePreview.Text = string.Join(Environment.NewLine, preview);

            _probeTexts.Clear();
            _pendingProbeEdits.Clear();
            _selectedEditableHit = it.Hit;
            ProbeOriginalText.Text = "";
            ProbeReplacementText.Text = "";
            if (it.Hit.Entry != null)
            {
                ProbeEditStatus.Text = "التعديل المباشر غير متاح للنصوص داخل الأرشيف حاليًا. استخرج الحاوية أولًا؛ وسيظل الملف الأصلي دون تغيير.";
            }
            else
            {
                foreach (var found in it.Hit.All.Where(s => BinaryTextMiner.IsRealSentence(s.Text) && s.ByteLength > 0).Take(500))
                    _probeTexts.Add(new ProbeEditItem(found));
                ProbeEditStatus.Text = $"تم تحميل {_probeTexts.Count} نص كحد أقصى للواجهة من ملف مستقل. اختر نصًا لبدء التعديل.";
            }
        }
    }

    private void ProbeEditList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProbeEditList.SelectedItem is not ProbeEditItem item) return;
        ProbeOriginalText.Text = item.Found.Text;
        ProbeReplacementText.Text = _pendingProbeEdits.TryGetValue(item.Found.Offset, out var edit)
            ? edit.Replacement : item.Found.Text;
    }

    private void QueueProbeEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEditableHit?.Entry != null || _selectedEditableHit == null)
        {
            ProbeEditStatus.Text = "اختار ملفًا مستقلًا من نتائج الفحص الذكي أولًا.";
            return;
        }
        if (ProbeEditList.SelectedItem is not ProbeEditItem item)
        {
            ProbeEditStatus.Text = "اختار النص المراد تعديله من القائمة.";
            return;
        }

        var replacement = ProbeReplacementText.Text;
        _pendingProbeEdits[item.Found.Offset] = new TextByteReplacement(
            item.Found.Offset, item.Found.ByteLength, item.Found.EncodingName, replacement, item.Found.Text);
        ProbeEditStatus.Text = $"تمت إضافة التعديل للقائمة. عدد التعديلات: {_pendingProbeEdits.Count}. سيتم التحقق من الترميز والطول عند حفظ النسخة.";
    }

    private async void SaveProbeEdits_Click(object sender, RoutedEventArgs e)
    {
        var hit = _selectedEditableHit;
        var edits = _pendingProbeEdits.Values.ToArray();
        var outputRoot = OutputBox.Text.Trim();
        if (hit == null || hit.Entry != null)
        {
            ProbeEditStatus.Text = "الميزة متاحة حاليًا لملف مستقل محدد، وليس عنصرًا داخل أرشيف.";
            return;
        }
        if (edits.Length == 0)
        {
            ProbeEditStatus.Text = "أضف تعديلًا واحدًا على الأقل قبل الحفظ.";
            return;
        }
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            ProbeEditStatus.Text = "حدد مجلد الناتج أولًا.";
            return;
        }

        SaveProbeEditsButton.IsEnabled = false;
        ProbeEditStatus.Text = "جاري التحقق وحفظ نسخة منفصلة…";
        try
        {
            var outputPath = await Task.Run(() =>
            {
                var sourcePath = hit.FullPath;
                var info = new FileInfo(sourcePath);
                const long maxSourceBytes = 300L * 1024 * 1024;
                if (!info.Exists || info.Length <= 0 || info.Length > maxSourceBytes)
                    throw new InvalidDataException("حجم الملف غير مناسب للتعديل التجريبي (الحد الأقصى 300 MiB).");

                var source = File.ReadAllBytes(sourcePath);
                if (source.LongLength > maxSourceBytes)
                    throw new InvalidDataException("حجم الملف تجاوز الحد المسموح أثناء القراءة.");
                var patched = BinaryTextPatcher.CreatePatchedCopy(source, edits);
                var outputFolder = Path.Combine(outputRoot, "_patched");
                Directory.CreateDirectory(outputFolder);

                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var unique = Guid.NewGuid().ToString("N")[..8];
                var output = Path.Combine(outputFolder,
                    Path.GetFileNameWithoutExtension(sourcePath) + ".flafyo-patched-" + stamp + "-" + unique + Path.GetExtension(sourcePath));
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(patched, 0, patched.Length);
                return output;
            });

            ProbeEditStatus.Text = $"تم حفظ نسخة منفصلة: {outputPath}. الملف الأصلي لم يتغير.";
            Log("تم حفظ نسخة ملف معدلة منفصلة: " + outputPath);
        }
        catch (Exception ex)
        {
            ProbeEditStatus.Text = "تعذر حفظ النسخة: " + ex.Message;
            AppLog.Write(ex);
        }
        finally
        {
            SaveProbeEditsButton.IsEnabled = true;
        }
    }

    private void ExportHit_Click(object sender, RoutedEventArgs e)
    {
        if (ProbeList.SelectedItem is not ProbeItem it) { ProbeStatus.Text = "اختار عنصر من القايمة الأول."; return; }
        try
        {
            var path = GameProbe.Export(it.Hit, Path.Combine(OutputBox.Text.Trim(), "_found"));
            ProbeStatus.Text = "اتصدّرت النصوص في: " + path;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ProbeStatus.Text = "فشل التصدير: " + ex.Message; AppLog.Write(ex); }
    }

    private void ExtractArc_Click(object sender, RoutedEventArgs e)
    {
        if (ProbeList.SelectedItem is not ProbeItem it) { ProbeStatus.Text = "اختار عنصر من قايمة الفحص الذكي الأول (ملف كامل، مش جزء جوه أرشيف)."; return; }
        var path = it.Hit.Entry == null ? it.Hit.FullPath : null;
        if (path == null) { ProbeStatus.Text = "العنصر ده جزء جوه حاوية. اختار الحاوية نفسها (السطر اللي اسم الملف فيه بس من غير فاصل ›)."; return; }
        try
        {
            var dir = Path.Combine(OutputBox.Text.Trim(), "_extracted", Path.GetFileNameWithoutExtension(path));
            var (kind, n) = ContainerTools.Extract(path, dir);
            ProbeStatus.Text = $"اتفك {n} ملف ({kind}) في: {dir}";
            ExtractedFolderBox.Text = dir; ContainerFileBox.Text = path;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ProbeStatus.Text = "فشل الاستخراج: " + ex.Message; AppLog.Write(ex); }
    }

    private void BrowseExtracted_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "اختر المجلد المستخرج" };
        if (dialog.ShowDialog(this) == true) ExtractedFolderBox.Text = dialog.FolderName;
    }

    private void BrowseContainer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "اختر ملف الحاوية الأصلي (.arc / .zip / .pak)" };
        if (dialog.ShowDialog(this) == true) ContainerFileBox.Text = dialog.FileName;
    }

    private void RepackApply_Click(object sender, RoutedEventArgs e)
    {
        var folder = ExtractedFolderBox.Text.Trim();
        var container = ContainerFileBox.Text.Trim();
        var outputRoot = OutputBox.Text.Trim();
        if (!Directory.Exists(folder) || !File.Exists(container))
        {
            StatusText.Text = "حدد المجلد المستخرج وملف الحاوية الأصلي الأول.";
            return;
        }
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            StatusText.Text = "حدد مجلد الناتج أولًا لحفظ نسخة الحاوية الجديدة.";
            return;
        }

        string? verificationFolder = null;
        string? verificationArchive = null;
        try
        {
            var originalKind = ContainerTools.Detect(File.ReadAllBytes(container));
            var rebuilt = ContainerTools.Repack(folder);
            if (ContainerTools.Detect(rebuilt) != originalKind)
                throw new InvalidDataException("نوع الحاوية الناتجة لا يطابق نوع الملف الأصلي.");

            // Re-open the rebuilt archive through the normal bounded extractor before making it available.
            verificationFolder = Path.Combine(Path.GetTempPath(), "flafyo-verify-" + Guid.NewGuid().ToString("N"));
            verificationArchive = Path.Combine(Path.GetTempPath(), "flafyo-verify-" + Guid.NewGuid().ToString("N") + Path.GetExtension(container));
            File.WriteAllBytes(verificationArchive, rebuilt);
            var (_, verifiedCount) = ContainerTools.Extract(verificationArchive, verificationFolder);
            if (verifiedCount == 0)
                throw new InvalidDataException("الحاوية الناتجة لا تحتوي على عناصر قابلة للتحقق.");

            var outputFolder = Path.Combine(outputRoot, "_rebuilt_containers");
            Directory.CreateDirectory(outputFolder);
            var outputPath = Path.Combine(outputFolder,
                Path.GetFileNameWithoutExtension(container) + ".flafyo-rebuilt-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] +
                Path.GetExtension(container));
            using (var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(rebuilt, 0, rebuilt.Length);

            RebuiltContainerBox.Text = outputPath;
            StatusText.Text = $"تم إنشاء نسخة حاوية منفصلة والتحقق من {verifiedCount} عنصر. الأصل لم يتغير. راجع النسخة قبل التطبيق.";
            Log("تم إنشاء نسخة حاوية منفصلة: " + outputPath);
        }
        catch (Exception ex)
        {
            RebuiltContainerBox.Text = "";
            StatusText.Text = "فشل بناء أو التحقق من النسخة: " + ex.Message;
            AppLog.Write(ex);
        }
        finally
        {
            if (verificationArchive != null && File.Exists(verificationArchive))
                try { File.Delete(verificationArchive); } catch { }
            if (verificationFolder != null && Directory.Exists(verificationFolder))
                try { Directory.Delete(verificationFolder, true); } catch { }
        }
    }

    private void ApplyRebuiltContainer_Click(object sender, RoutedEventArgs e)
    {
        var original = ContainerFileBox.Text.Trim();
        var rebuilt = RebuiltContainerBox.Text.Trim();
        if (!File.Exists(original) || !File.Exists(rebuilt))
        {
            StatusText.Text = "أنشئ نسخة حاوية منفصلة وتأكد من تحديد الملف الأصلي أولًا.";
            return;
        }

        try
        {
            var originalKind = ContainerTools.Detect(File.ReadAllBytes(original));
            var rebuiltKind = ContainerTools.Detect(File.ReadAllBytes(rebuilt));
            if (originalKind == ContainerKind.None || rebuiltKind != originalKind)
                throw new InvalidDataException("نوع النسخة الجديدة لا يطابق نوع الحاوية الأصلية.");

            var answer = MessageBox.Show(this,
                "سيتم استبدال الحاوية الأصلية بالنسخة التي أنشأتها. ستُحفظ نسخة احتياطية بجوار الأصل باسم .flafyo_backup. تأكد أن اللعبة مغلقة. هل تريد المتابعة؟",
                "تأكيد تطبيق التعديل", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            var backup = ContainerTools.Apply(original, File.ReadAllBytes(rebuilt));
            StatusText.Text = $"تم تطبيق النسخة. النسخة الاحتياطية للأصل: {backup}";
            Log("تم تطبيق الحاوية المعدلة بعد التأكيد: " + original);
        }
        catch (Exception ex)
        {
            StatusText.Text = "تعذر تطبيق النسخة: " + ex.Message;
            AppLog.Write(ex);
        }
    }

    private void SelectSuggested_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsSelected = i.Suggested; FilesList.Items.Refresh(); }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsSelected = true; FilesList.Items.Refresh(); }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsSelected = false; FilesList.Items.Refresh(); }

    private static string Eta(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} ساعة و{t.Minutes} دقيقة" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} دقيقة" : $"{t.Seconds} ثانية";

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = _items.Where(i => i.IsSelected).Select(i => i.Info).ToList();
        if (selected.Count == 0) { StatusText.Text = "مفيش ملفات متحددة. افحص الملفات الأول وحدد منها."; return; }
        var output = OutputBox.Text.Trim();
        if (output.Length == 0) { StatusText.Text = "حدد مكان للملفات المترجمة."; return; }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        ITranslator? translator = null;
        try
        {
            StatusText.Text = "جاري تجهيز نموذج الترجمة… (الحزمة التقيلة ممكن تاخد دقيقة)";
            EtaText.Text = "";
            var (resolver, tr) = _factory(Log);
            translator = tr;

            // 1) a first sentence proves that the engine works and shows how long one sentence takes
            var sw = Stopwatch.StartNew();
            string sample;
            try
            {
                var r = await resolver.ResolveAsync("Welcome back, traveler.", _cts.Token).WaitAsync(TimeSpan.FromMinutes(3));
                sample = r.Text;
            }
            catch (TimeoutException)
            {
                StatusText.Text = "محرك الترجمة مردّش خلال 3 دقايق. شوف في الشاشة الرئيسية السطر البرتقالي، وجرّب الحزمة الخفيفة.";
                return;
            }
            Log($"اختبار المحرك: «Welcome back, traveler.» → «{sample}»  ({sw.Elapsed.TotalSeconds:0.0} ثانية)");
            if (string.IsNullOrWhiteSpace(sample)) { StatusText.Text = "المحرك رجّع ترجمة فاضية. شوف سجل الأخطاء."; return; }

            // 2) the real run
            int maxPer = QuickBox.IsChecked == true ? 30 : 0;
            var totals = selected.Select(f => maxPer > 0 ? Math.Min(f.StringCount, maxPer) : f.StringCount).ToArray();
            var offsets = new int[totals.Length];
            for (int i = 1; i < totals.Length; i++) offsets[i] = offsets[i - 1] + totals[i - 1];
            int grand = Math.Max(1, totals.Sum());
            var clock = Stopwatch.StartNew();

            var engine = new GameTextTranslator(resolver);
            var progress = new Progress<GameProgress>(p =>
            {
                int idx = p.FileIndex - 1;
                int done = offsets[idx] + Math.Min(p.Done, totals[idx]);
                Bar.Value = 100.0 * done / grand;
                StatusText.Text = $"ملف {p.FileIndex} من {p.FileCount}: {p.File}   ({done} من {grand} نص)   أخطاء: {p.Errors}";
                if (done > 5)
                {
                    double perSecond = done / Math.Max(1, clock.Elapsed.TotalSeconds);
                    EtaText.Text = $"السرعة: {perSecond:0.0} نص في الثانية — المتبقي حوالي {Eta(TimeSpan.FromSeconds((grand - done) / Math.Max(0.01, perSecond)))}";
                }
            });
            var options = new GameTranslateOptions { ShapeArabic = ShapeBox.IsChecked == true, MaxStringsPerFile = maxPer, Log = Log };
            var report = await engine.TranslateAsync(selected, output, options, progress, _cts.Token);
            Bar.Value = 100;
            StatusText.Text = $"خلصت: {report.Files} ملف، {report.Strings} نص ({report.FromMemory} من الذاكرة)، أخطاء: {report.Errors}، ملفات اتخطت: {report.SkippedFiles}، الوقت: {report.Elapsed:hh\\:mm\\:ss}";
            EtaText.Text = maxPer > 0 ? "ده كان اختبار سريع. راجع الناتج، ولو تمام شيل علامة «اختبار سريع» وأعد الترجمة (اللي اتترجم محفوظ ومش هيتعاد)." : "";
            Log("الملفات المترجمة في: " + output);
        }
        catch (OperationCanceledException) { StatusText.Text = "اتوقفت. اللي اتترجم محفوظ، وتقدر تكمل بعدين من نفس النقطة."; }
        catch (Exception ex) { StatusText.Text = "فشلت الترجمة: " + ex.Message; Log(ex.ToString()); AppLog.Write(ex); }
        finally
        {
            (translator as IDisposable)?.Dispose();
            _cts?.Dispose(); _cts = null;
            SetBusy(false);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        var dir = OutputBox.Text.Trim();
        if (!Directory.Exists(dir)) { StatusText.Text = "مفيش مجلد ناتج لسه."; return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch { }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var game = FolderBox.Text; var output = OutputBox.Text.Trim();
        if (!Directory.Exists(game) || !Directory.Exists(output)) { StatusText.Text = "لازم تترجم الأول (ويبقى فيه مجلد ناتج)."; return; }
        var answer = MessageBox.Show(this,
            "هتتبدل ملفات اللعبة بالنسخة المترجمة، وهيتعمل نسخة احتياطية من الأصل جوه مجلد FLAFYO_backup في اللعبة.\n\n" +
            "استخدمها في الألعاب الفردية بس. اللعبة لازم تكون مقفولة. تكمل؟",
            "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            int n = GameInstaller.Apply(output, game);
            StatusText.Text = $"اتبدلت {n} ملف. النسخة الأصلية في: {GameInstaller.BackupRoot(game)}";
        }
        catch (Exception ex) { StatusText.Text = "فشل التطبيق: " + ex.Message; AppLog.Write(ex); }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var game = FolderBox.Text;
        if (!Directory.Exists(game)) { StatusText.Text = "اختر مجلد اللعبة."; return; }
        try
        {
            int n = GameInstaller.Restore(game);
            StatusText.Text = n == 0 ? "مفيش نسخة احتياطية في المجلد ده." : $"اترجّع {n} ملف للأصل.";
        }
        catch (Exception ex) { StatusText.Text = "فشل الاسترجاع: " + ex.Message; AppLog.Write(ex); }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
        _probeCts?.Cancel();
        base.OnClosing(e);
    }
}
