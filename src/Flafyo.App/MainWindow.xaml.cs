using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xF1A, WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000, VkT = 0x54;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private object _regionBox = System.Drawing.Rectangle.Empty;   // boxed so reads/writes are atomic across threads
    private volatile int _intervalMs = 200;
    private volatile bool _masculine = true;
    private IReadOnlyDictionary<string, string> _masculineMap = new Dictionary<string, string>();
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _ready;
    private OverlayWindow? _overlay;
    private TranslationPipeline? _pipeline;
    private RegionCaptureWorker? _capture;
    private ITranslator? _translator;
    private bool _downloading;
    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        UserData.EnsureTemplates();
        if (_settings.Width >= 20 && _settings.Height >= 10)
        {
            Volatile.Write(ref _regionBox, (object)new System.Drawing.Rectangle(_settings.X, _settings.Y, _settings.Width, _settings.Height));
            RegionText.Text = $"المنطقة المحفوظة: {_settings.Width}×{_settings.Height}";
        }
        SpeedBox.SelectedIndex = Math.Clamp(_settings.SpeedIndex, 0, 2);
        TextSizeBox.SelectedIndex = Math.Clamp(_settings.TextSizeIndex, 0, 2);
        PackBox.SelectedIndex = Math.Clamp(_settings.PackIndex, 0, 1);
        MasculineBox.IsChecked = _settings.Masculine;
        PreciseBox.IsChecked = _settings.Precise;
        _masculine = _settings.Masculine;
        // restore the last window size, but never bigger than the screen
        if (_settings.WindowWidth >= MinWidth && _settings.WindowHeight >= MinHeight)
        {
            Width = _settings.WindowWidth; Height = _settings.WindowHeight;
        }
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, work.Width - 20);
        Height = Math.Min(Height, work.Height - 20);
        _ready = true;
        UpdatePackStatus();
        SourceInitialized += OnSourceInitialized;
    }

    private System.Drawing.Rectangle Region => (System.Drawing.Rectangle)Volatile.Read(ref _regionBox);

    private void SetStatus(string s) => Dispatcher.InvokeAsync(() => StatusText.Text = s);

    private void ShowEngine(string engine, string? error, string label) => Dispatcher.InvokeAsync(() =>
        EngineText.Text = engine switch
        {
            "loading" => $"المحرك: جاري تحميل الحزمة {label}…",
            "offline" => $"المحرك: الذكاء الاصطناعي المحلي — الحزمة {label} ✅",
            "fallback" => $"⚠️ الحزمة {label} فشلت، فبيستخدم البديل. السبب: {error}",
            "online" => "المحرك: ترجمة أونلاين مؤقتة (مفيش حزمة مثبتة)",
            _ => $"⚠️ فشل تحميل المحرك: {error}"
        });

    // ---- global hotkey (registered with Windows; no keyboard hook, game input is untouched) ----
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this);
        _hwnd = src.Handle;
        src.AddHook(WndProc);
        if (!RegisterHotKey(_hwnd, HotkeyId, ModControl | ModAlt | ModNoRepeat, VkT))
            HotkeyText.Text = "تعذّر تسجيل الاختصار (برنامج تاني بيستخدمه). استخدم الأزرار.";
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId) { handled = true; ToggleTranslation(); }
        return IntPtr.Zero;
    }

    private async void ToggleTranslation()
    {
        if (_pipeline == null) StartTranslation(); else await StopAsync();
    }

    // ---- language packs ----
    private void UpdatePackStatus()
    {
        bool light = LanguagePack.Light.IsInstalled(), heavy = LanguagePack.Heavy.IsInstalled();
        LightText.Text = light ? "✅ الحزمة الخفيفة مثبتة (سريعة)" : "الحزمة الخفيفة (حوالي 145 ميجا): مش مثبتة";
        HeavyText.Text = heavy ? "✅ الحزمة التقيلة مثبتة (أدق)" : "الحزمة التقيلة (حوالي 0.9 جيجا): مش مثبتة";
        LightButton.Visibility = light ? Visibility.Collapsed : Visibility.Visible;
        HeavyButton.Visibility = heavy ? Visibility.Collapsed : Visibility.Visible;
        LightButton.IsEnabled = HeavyButton.IsEnabled = !_downloading;
    }

    private async Task DownloadPackAsync(LanguagePack pack, TextBlock text)
    {
        if (_downloading) return;
        _downloading = true; LightButton.IsEnabled = HeavyButton.IsEnabled = false;
        string? error = null;
        var progress = new Progress<double>(v => text.Text = $"جاري تنزيل الحزمة {pack.ShortName}… {v * 100:0}%");
        try { await pack.DownloadAsync(progress, CancellationToken.None); }
        catch (Exception ex) { error = "فشل التنزيل: " + ex.Message + " (جرّب تاني)"; }
        finally { _downloading = false; UpdatePackStatus(); }
        if (error != null) text.Text = error;
    }

    private async void Light_Click(object sender, RoutedEventArgs e) => await DownloadPackAsync(LanguagePack.Light, LightText);
    private async void Heavy_Click(object sender, RoutedEventArgs e) => await DownloadPackAsync(LanguagePack.Heavy, HeavyText);

    private void Pack_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.PackIndex = PackBox.SelectedIndex; _settings.Save();
    }

    private void Masculine_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _masculine = MasculineBox.IsChecked == true;
        _settings.Masculine = _masculine; _settings.Save();
    }

    // ---- translation strip appearance ----
    private OverlayWindow EnsureOverlay()
    {
        _overlay ??= new OverlayWindow();
        _overlay.ApplyStyle(_settings.Style);
        return _overlay;
    }

    private System.Drawing.Rectangle PreviewRegion() =>
        Region.Width >= 20 && Region.Height >= 10 ? Region : OverlayWindow.DefaultPreviewRegion();

    private System.Drawing.Rectangle? PickOverlayArea()
    {
        var selector = new RegionSelectorWindow("اسحب بالماوس لتحديد مكان وحجم شريط الترجمة. Esc للإلغاء");
        return selector.ShowDialog() == true ? selector.SelectedRegion : (System.Drawing.Rectangle?)null;
    }

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        var overlay = EnsureOverlay();
        var win = new AppearanceWindow(_settings.Style, PickOverlayArea,
            changed: () => { _settings.Save(); overlay.ApplyStyle(_settings.Style); overlay.ShowPreview(PreviewRegion()); },
            preview: () => overlay.ShowPreview(PreviewRegion()))
        { Owner = this };
        win.Closed += (_, _) => { if (_pipeline == null) overlay.Hide(); };
        win.Show();
    }

    private void Precise_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.Precise = PreciseBox.IsChecked == true; _settings.Save();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        UserData.EnsureTemplates();
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{UserData.Dir}\"") { UseShellExecute = true }); } catch { }
    }

    // ---- region + speed + text size ----
    private void Select_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        var selector = new RegionSelectorWindow();
        bool ok = selector.ShowDialog() == true;
        WindowState = WindowState.Normal;
        Activate();
        if (!ok) return;
        var r = selector.SelectedRegion;
        Volatile.Write(ref _regionBox, (object)r);
        RegionText.Text = $"المنطقة: {r.Width}×{r.Height}";
        _settings.X = r.X; _settings.Y = r.Y; _settings.Width = r.Width; _settings.Height = r.Height;
        _settings.Save();
    }

    private void Speed_Changed(object sender, SelectionChangedEventArgs e)
    {
        _intervalMs = SpeedBox.SelectedIndex switch { 0 => 400, 2 => 100, _ => 200 };
        if (!_ready) return;
        _settings.SpeedIndex = SpeedBox.SelectedIndex; _settings.Save();
    }

    private void TextSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.TextSizeIndex = TextSizeBox.SelectedIndex; _settings.Save();
    }

    private int TextScale() => TextSizeBox.SelectedIndex switch { 0 => 1, 2 => 3, _ => 2 };

    // ---- start / stop ----
    private void Start_Click(object sender, RoutedEventArgs e) => StartTranslation();

    private void StartTranslation()
    {
        if (_pipeline != null) return;
        if (Region.Width < 20 || Region.Height < 10) { StatusText.Text = "اختر منطقة النص أولًا (الزر الأول)"; return; }

        var ocr = new WindowsOcrEngine("en-US", TextScale());
        if (!ocr.IsAvailable)
        {
            StatusText.Text = "حزمة OCR الإنجليزية غير مثبتة: Settings ← Time & language ← Language & region ← أضف English";
            return;
        }

        var sink = new WpfOverlaySink(EnsureOverlay(), () => Region);

        // chosen pack → (if missing) the other pack → (if none) the temporary online service
        int threads = SpeedBox.SelectedIndex == 0 ? 1 : 2;   // few CPU threads: the game keeps priority
        int beams = SpeedBox.SelectedIndex != 0 && PreciseBox.IsChecked == true ? 3 : 1;   // beam search only on request
        var chosen = PackBox.SelectedIndex == 1 ? LanguagePack.Heavy : LanguagePack.Light;
        var other = chosen.Kind == PackKind.Heavy ? LanguagePack.Light : LanguagePack.Heavy;
        var pack = chosen.IsInstalled() ? chosen : other.IsInstalled() ? other : null;

        ITranslator inner; string modeText;
        if (pack != null)
        {
            var backup = pack == chosen ? other : chosen;
            bool backupInstalled = backup.IsInstalled();
            var lazy = new LazyTranslator(
                () => pack.CreateTranslator(threads, beams),
                () => backupInstalled ? backup.CreateTranslator(threads, beams) : new OnlineTranslator("en", "ar"));
            lazy.EngineReady += (eng, err) => ShowEngine(eng, err, pack.ShortName);
            ShowEngine(lazy.Engine, lazy.Error, pack.ShortName);   // in case it finished before we subscribed
            inner = lazy; modeText = "بدون إنترنت";
        }
        else
        {
            inner = new OnlineTranslator("en", "ar");
            ShowEngine("online", null, "");
            modeText = "بالإنترنت (مؤقتًا)";
        }

        // the user's own masculine.txt is added to the built-in list
        try { _masculineMap = ArabicGender.ParseUserFile(File.ReadAllLines(UserData.MasculineFile)); } catch { }
        _translator = new PostProcessTranslator(inner, s => _masculine ? ArabicGender.MakeMasculine(s, _masculineMap) : s);

        UserData.EnsureTemplates();
        var resolver = new TranslationResolver(_translator, new TranslationCache(),
            BuiltInDictionary.Load(UserData.DictionaryFile), IdiomRewriter.Load(UserData.IdiomsFile), Phrasebook.LoadEmbedded());

        // wait a little before translating so half-typed lines are not translated on their own
        var options = new PipelineOptions
        {
            SettleDelay = TimeSpan.FromMilliseconds(SpeedBox.SelectedIndex switch { 0 => 500, 2 => 220, _ => 350 })
        };
        var p = new TranslationPipeline(ocr, resolver, sink, options, s => SetStatus(s));
        p.Latency += l => Dispatcher.InvokeAsync(() =>
            LatencyText.Text = $"OCR {l.OcrMs:0} ms | ترجمة {l.TranslationMs:0} ms | الإجمالي {l.TotalMs:0} ms");
        p.Translated += (_, src, ar) =>
        {
            Dispatcher.InvokeAsync(() => LastText.Text = $"المقروء: {src}\nالترجمة: {ar}");
            AppendHistory(src, ar);
        };
        p.Start();
        _pipeline = p;

        _capture = new RegionCaptureWorker(p, () => Region, () => _intervalMs, 1, s => SetStatus(s));
        _capture.Start();

        StatusText.Text = "الترجمة شغّالة — " + modeText;
        StartButton.IsEnabled = false; StopButton.IsEnabled = true; SelectButton.IsEnabled = false;
    }

    private static void AppendHistory(string src, string ar) => Task.Run(() =>
    {
        try
        {
            var f = new FileInfo(UserData.HistoryFile);
            if (f.Exists && f.Length > 1_000_000) f.Delete();   // keep it small
            File.AppendAllText(UserData.HistoryFile, $"{DateTime.Now:HH:mm:ss}  {src}  =>  {ar}{Environment.NewLine}", System.Text.Encoding.UTF8);
        }
        catch { }
    });

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopAsync();

    private async Task StopAsync()
    {
        var c = _capture; var p = _pipeline;
        _capture = null; _pipeline = null;
        c?.Dispose();
        if (p != null) await p.DisposeAsync();
        (_translator as IDisposable)?.Dispose(); _translator = null;
        _overlay?.Hide();
        EngineText.Text = "";
        LastText.Text = "";
        StatusText.Text = "متوقف";
        StartButton.IsEnabled = true; StopButton.IsEnabled = false; SelectButton.IsEnabled = true;
        UpdatePackStatus();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (WindowState == WindowState.Normal && ActualWidth > 0)
        {
            _settings.WindowWidth = ActualWidth; _settings.WindowHeight = ActualHeight; _settings.Save();
        }
        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId);
        _capture?.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
    }
}
