using System.Collections.ObjectModel;
using System.Diagnostics;
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
        base.OnClosing(e);
    }
}
