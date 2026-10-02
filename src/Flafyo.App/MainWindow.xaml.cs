using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

public partial class MainWindow : Window
{
    private object _regionBox = System.Drawing.Rectangle.Empty;   // boxed so reads/writes are atomic across threads
    private volatile int _intervalMs = 200;
    private OverlayWindow? _overlay;
    private TranslationPipeline? _pipeline;
    private RegionCaptureWorker? _capture;
    private OnlineTranslator? _translator;

    public MainWindow() { InitializeComponent(); }

    private System.Drawing.Rectangle Region => (System.Drawing.Rectangle)Volatile.Read(ref _regionBox);

    private void SetStatus(string s) => Dispatcher.InvokeAsync(() => StatusText.Text = s);

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        var selector = new RegionSelectorWindow();
        bool ok = selector.ShowDialog() == true;
        WindowState = WindowState.Normal;
        Activate();
        if (!ok) return;
        Volatile.Write(ref _regionBox, (object)selector.SelectedRegion);
        RegionText.Text = $"المنطقة: {selector.SelectedRegion.Width}×{selector.SelectedRegion.Height}";
    }

    private void Speed_Changed(object sender, SelectionChangedEventArgs e) =>
        _intervalMs = SpeedBox.SelectedIndex switch { 0 => 400, 2 => 100, _ => 200 };

    private void Start_Click(object sender, RoutedEventArgs e)
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
        var dictFile = Path.Combine(AppContext.BaseDirectory, "dictionary.txt");
        _translator = new OnlineTranslator("en", "ar");
        var resolver = new TranslationResolver(_translator, new TranslationCache(), BuiltInDictionary.Load(dictFile));

        var p = new TranslationPipeline(ocr, resolver, sink, log: s => SetStatus(s));
        p.Latency += l => Dispatcher.InvokeAsync(() =>
            LatencyText.Text = $"OCR {l.OcrMs:0} ms | ترجمة {l.TranslationMs:0} ms | الإجمالي {l.TotalMs:0} ms");
        p.Start();
        _pipeline = p;

        _capture = new RegionCaptureWorker(p, () => Region, () => _intervalMs, 1, s => SetStatus(s));
        _capture.Start();

        StatusText.Text = "الترجمة شغّالة";
        StartButton.IsEnabled = false; StopButton.IsEnabled = true; SelectButton.IsEnabled = false;
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopAsync();

    private async Task StopAsync()
    {
        var c = _capture; var p = _pipeline;
        _capture = null; _pipeline = null;
        c?.Dispose();
        if (p != null) await p.DisposeAsync();
        _translator?.Dispose(); _translator = null;
        _overlay?.Hide();
        StatusText.Text = "متوقف";
        StartButton.IsEnabled = true; StopButton.IsEnabled = false; SelectButton.IsEnabled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _capture?.Dispose();
        _overlay?.Close();
        base.OnClosed(e);
    }
}
