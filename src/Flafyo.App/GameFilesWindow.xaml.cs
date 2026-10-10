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
        public bool IsSelected { get; set; } = true;
        public string Text => $"{Info.RelativePath}   —   {Info.StringCount} نص   [{Info.Format}]";
        public FileItem(GameFileInfo info) => Info = info;
    }

    private readonly AppSettings _settings;
    private readonly Func<(TranslationResolver Resolver, ITranslator Translator)> _factory;
    private readonly ObservableCollection<FileItem> _items = new();
    private CancellationTokenSource? _cts;
    private bool _busy;

    public GameFilesWindow(AppSettings settings, Func<(TranslationResolver Resolver, ITranslator Translator)> factory)
    {
        InitializeComponent();
        _settings = settings; _factory = factory;
        FilesList.ItemsSource = _items;
        if (!string.IsNullOrEmpty(settings.GameFolder) && Directory.Exists(settings.GameFolder)) SetFolder(settings.GameFolder);
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
        _items.Clear(); CountText.Text = "";
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

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var folder = FolderBox.Text;
        if (!Directory.Exists(folder)) { StatusText.Text = "اختر مجلد اللعبة الأول."; return; }
        SetBusy(true); StopButton.IsEnabled = false;
        _items.Clear(); StatusText.Text = "جاري فحص الملفات…";
        try
        {
            var options = new GameScanOptions { IncludePlainText = PlainBox.IsChecked == true };
            var files = await Task.Run(() => GameScanner.Scan(folder, options, CancellationToken.None, Log));
            foreach (var f in files) _items.Add(new FileItem(f));
            int strings = files.Sum(f => f.StringCount);
            CountText.Text = $"لقيت {files.Count} ملف فيهم {strings} نص قابل للترجمة.";
            StatusText.Text = files.Count == 0
                ? "مفيش ملفات نصية مفهومة. الألعاب كتير بتحط النصوص في ملفات مضغوطة أو بصيغ خاصة، والبرنامج مش بيفكها."
                : "حدد الملفات اللي عايز تترجمها واضغط ابدأ.";
        }
        catch (Exception ex) { StatusText.Text = "فشل الفحص: " + ex.Message; }
        finally { SetBusy(false); }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsSelected = true; FilesList.Items.Refresh(); }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.IsSelected = false; FilesList.Items.Refresh(); }

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
            StatusText.Text = "جاري تجهيز نموذج الترجمة…";
            var (resolver, tr) = _factory();
            translator = tr;
            var engine = new GameTextTranslator(resolver);
            var progress = new Progress<GameProgress>(p =>
            {
                double fraction = (p.FileIndex - 1 + (double)p.Done / Math.Max(1, p.Total)) / Math.Max(1, p.FileCount);
                Bar.Value = fraction * 100;
                StatusText.Text = $"ملف {p.FileIndex} من {p.FileCount}: {p.File}  ({p.Done}/{p.Total})   أخطاء: {p.Errors}";
            });
            var options = new GameTranslateOptions { ShapeArabic = ShapeBox.IsChecked == true };
            var report = await engine.TranslateAsync(selected, output, options, progress, _cts.Token);
            Bar.Value = 100;
            StatusText.Text = $"خلصت: {report.Files} ملف، {report.Strings} نص ({report.FromMemory} من الذاكرة)، أخطاء: {report.Errors}، الوقت: {report.Elapsed:hh\\:mm\\:ss}";
            Log("الملفات المترجمة في: " + output);
        }
        catch (OperationCanceledException) { StatusText.Text = "اتوقفت. اللي اتترجم محفوظ، وتقدر تكمل بعدين من نفس النقطة."; }
        catch (Exception ex) { StatusText.Text = "فشلت الترجمة: " + ex.Message; Log(ex.ToString()); }
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
        catch (Exception ex) { StatusText.Text = "فشل التطبيق: " + ex.Message; }
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
        catch (Exception ex) { StatusText.Text = "فشل الاسترجاع: " + ex.Message; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
        base.OnClosing(e);
    }
}
