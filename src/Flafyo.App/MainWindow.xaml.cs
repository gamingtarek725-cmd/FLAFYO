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
        _ready = true;
        UpdatePackStatus();
        SourceInitialized += OnSourceInitialized;
    }

    private System.Drawing.Rectangle Region => (System.Drawing.Rectangle)Volatile.Read(ref _regionBox);

    private void SetStatus(string s) => Dispatcher.InvokeAsync(() => StatusText.Text = s);

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

    private void ShowEngine(string engine, string? error) => Dispatcher.InvokeAsync(() =>
        EngineText.Text = engine switch
        {
            "loading" => "المحرك: جاري تحميل النموذج المحلي…",
            "offline" => "المحرك: الذكاء الاصطناعي المحلي ✅",
            "online-fallback" => $"⚠️ النموذج المحلي فشل، فبيترجم بالإنترنت (حرفية). السبب: {error}",
            "online" => "المحرك: ترجمة أونلاين مؤقتة (الحزمة غير مثبتة)",
            _ => $"⚠️ فشل تحميل المحرك: {error}"
        });

    // ---- language pack ----
    private void UpdatePackStatus()
    {
        bool installed = ModelPack.IsInstalled(ModelPack.DefaultDir);
        PackText.Text = installed
            ? "✅ الحزمة مثبتة. الترجمة هتشتغل بدون إنترنت."
            : "الحزمة مش مثبتة. لحد ما تنزّلها هتشتغل الترجمة بالإنترنت (مؤقتًا).";
        PackButton.IsEnabled = !installed && !_downloading;
        PackButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Pack_Click(object sender, RoutedEventArgs e)
    {
        if (_downloading) return;
        _downloading = true; PackButton.IsEnabled = false;
        var progress = new Progress<double>(v => PackText.Text = $"جاري التنزيل… {v * 100:0}%");
        try { await ModelPack.DownloadAsync(ModelPack.DefaultDir, progress, CancellationToken.None); }
        catch (Exception ex) { PackText.Text = "فشل التنزيل: " + ex.Message + " (جرّب تاني)"; }
        finally { _downloading = false; UpdatePackStatus(); }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        UserData.EnsureTemplates();
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{UserData.Dir}\"") { UseShellExecute = true }); } catch { }
    }

    // ---- region + speed ----
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

    // ---- start / stop ----
    private void Start_Click(object sender, RoutedEventArgs e) => StartTranslation();

    private void StartTranslation()
    {
        if (_pipeline != null) return;
        if (Region.Width < 20 || Region.Height < 10) { StatusText.Text = "اختر منطقة النص أولًا (الزر الأول)"; return; }

        var ocr = new WindowsOcrEngine("en-US");
        if (!ocr.IsAvailable)
        {
            StatusText.Text = "حزمة OCR الإنجليزية غير مثبتة: Settings ← Time & language ← Language & region ← أضف English";
            return;
        }

        _overlay ??= new OverlayWindow();
        var sink = new WpfOverlaySink(_overlay, () => Region);

        // Offline AI model if the language pack is installed (loads in the background); otherwise the temporary online service.
        string modeText;
        var modelDir = ModelPack.DefaultDir;
        if (ModelPack.IsInstalled(modelDir))
        {
            int threads = SpeedBox.SelectedIndex == 0 ? 1 : 2;   // few CPU threads: the game keeps priority
            var lazy = new LazyTranslator(() => OnnxMarianTranslator.Load(modelDir, threads),
                                          () => new OnlineTranslator("en", "ar"));
            lazy.EngineReady += ShowEngine;
            ShowEngine(lazy.Engine, lazy.Error);   // in case it finished before we subscribed
            _translator = lazy;
            modeText = "بدون إنترنت";
        }
        else
        {
            _translator = new OnlineTranslator("en", "ar");
            ShowEngine("online", null);
            modeText = "بالإنترنت (مؤقتًا)";
        }

        UserData.EnsureTemplates();
        var resolver = new TranslationResolver(_translator, new TranslationCache(),
            BuiltInDictionary.Load(UserData.DictionaryFile), IdiomRewriter.Load(UserData.IdiomsFile));

        var p = new TranslationPipeline(ocr, resolver, sink, log: s => SetStatus(s));
        p.Latency += l => Dispatcher.InvokeAsync(() =>
            LatencyText.Text = $"OCR {l.OcrMs:0} ms | ترجمة {l.TranslationMs:0} ms | الإجمالي {l.TotalMs:0} ms");
        p.Start();
        _pipeline = p;

        _capture = new RegionCaptureWorker(p, () => Region, () => _intervalMs, 1, s => SetStatus(s));
        _capture.Start();

        StatusText.Text = "الترجمة شغّالة — " + modeText;
        StartButton.IsEnabled = false; StopButton.IsEnabled = true; SelectButton.IsEnabled = false;
    }

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
        StatusText.Text = "متوقف";
        StartButton.IsEnabled = true; StopButton.IsEnabled = false; SelectButton.IsEnabled = true;
        UpdatePackStatus();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, HotkeyId);
        _capture?.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
    }
}
