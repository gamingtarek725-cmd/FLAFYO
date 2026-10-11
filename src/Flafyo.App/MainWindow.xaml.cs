using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Flafyo.Core;
using Flafyo.Windows;

namespace Flafyo.Desktop;

public partial class MainWindow : Window
{
    private const int HotkeyToggle = 0xF1A, HotkeyScan = 0xF1B, HotkeyCards = 0xF1C, HotkeySpeak = 0xF1D, WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000, VkT = 0x54, VkS = 0x53, VkH = 0x48, VkR = 0x52;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    private object _regionBox = System.Drawing.Rectangle.Empty;   // boxed so reads/writes are atomic across threads
    private volatile int _intervalMs = 200;
    private volatile bool _masculine = true;
    private volatile string _lastArabic = "";
    private IReadOnlyDictionary<string, string> _masculineMap = new Dictionary<string, string>();
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _ready;
    private OverlayWindow? _overlay;
    private ScreenOverlayWindow? _screenWindow;
    private TranslationPipeline? _pipeline;
    private ScreenPipeline? _screen;
    private RegionCaptureWorker? _capture;
    private IScreenGrabber? _grabber;
    private ITranslator? _translator;
    private ArabicSpeaker? _speaker;
    private List<MonitorInfo> _monitors = new();
    private List<GameProfile> _profiles = new();
    private List<string> _runningExes = new();
    private GameProfile? _activeProfile;
    private readonly HashSet<string> _loggedBlocks = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _watcher = new() { Interval = TimeSpan.FromSeconds(2.5) };
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
        ModeBox.SelectedIndex = Math.Clamp(_settings.ModeIndex, 0, 1);
        CaptureBox.SelectedIndex = Math.Clamp(_settings.CaptureIndex, 0, 1);
        ContinuousBox.IsChecked = _settings.Continuous;
        MasculineBox.IsChecked = _settings.Masculine;
        PreciseBox.IsChecked = _settings.Precise;
        SpeakBox.IsChecked = _settings.Speak;
        AutoProfileBox.IsChecked = _settings.AutoProfile;
        _masculine = _settings.Masculine;

        try { _monitors = Monitors.GetAll(); } catch { }
        MonitorBox.ItemsSource = _monitors.Select(m => m.Name).ToList();
        MonitorBox.SelectedIndex = _monitors.Count == 0 ? -1 : Math.Clamp(_settings.MonitorIndex, 0, _monitors.Count - 1);

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
        RefreshProfiles();
        RefreshRunning();
        _watcher.Tick += (_, _) => WatcherTick();
        _watcher.Start();
        SourceInitialized += OnSourceInitialized;
    }

    private System.Drawing.Rectangle Region => (System.Drawing.Rectangle)Volatile.Read(ref _regionBox);
    private bool ScreenMode => ModeBox.SelectedIndex == 1;

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

    // ---- global hotkeys (registered with Windows; no keyboard hook, game input is untouched) ----
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this);
        _hwnd = src.Handle;
        src.AddHook(WndProc);
        const uint mods = ModControl | ModAlt | ModNoRepeat;
        bool ok = RegisterHotKey(_hwnd, HotkeyToggle, mods, VkT);
        RegisterHotKey(_hwnd, HotkeyScan, mods, VkS);
        RegisterHotKey(_hwnd, HotkeyCards, mods, VkH);
        RegisterHotKey(_hwnd, HotkeySpeak, mods, VkR);
        if (!ok) HotkeyText.Text = "تعذّر تسجيل الاختصارات (برنامج تاني بيستخدمها). استخدم الأزرار.";
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            switch (wParam.ToInt32())
            {
                case HotkeyToggle: handled = true; ToggleTranslation(); break;
                case HotkeyScan: handled = true; _screen?.ScanNow(); break;
                case HotkeyCards: handled = true; _screenWindow?.ToggleVisible(); break;
                case HotkeySpeak: handled = true; _ = SpeakAsync(_lastArabic); break;
            }
        }
        return IntPtr.Zero;
    }

    private async void ToggleTranslation()
    {
        if (_pipeline == null && _screen == null) StartTranslation(); else await StopAsync();
    }

    // ---- reading the translation aloud ----
    private ArabicSpeaker? EnsureSpeaker()
    {
        try { return _speaker ??= new ArabicSpeaker(); } catch { return null; }
    }

    private async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { var s = EnsureSpeaker(); if (s != null) await s.SpeakAsync(text); } catch { }
    }

    private void Speak_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.Speak = SpeakBox.IsChecked == true; _settings.Save();
        if (!_settings.Speak) { SpeakText.Text = ""; _speaker?.Stop(); return; }
        var s = EnsureSpeaker();
        SpeakText.Text = s is { HasArabicVoice: true }
            ? $"الصوت: {s.VoiceName}"
            : "مفيش صوت عربي على جهازك. ضيفه من: Settings ← Time & language ← Speech ← Add voices ← Arabic.";
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

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.ModeIndex = ModeBox.SelectedIndex; _settings.Save();
    }

    private void Capture_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.CaptureIndex = CaptureBox.SelectedIndex; _settings.Save();
    }

    private void Monitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || MonitorBox.SelectedIndex < 0) return;
        _settings.MonitorIndex = MonitorBox.SelectedIndex; _settings.Save();
    }

    private void Continuous_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.Continuous = ContinuousBox.IsChecked == true; _settings.Save();
    }

    private void Precise_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.Precise = PreciseBox.IsChecked == true; _settings.Save();
    }

    private void AutoProfile_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.AutoProfile = AutoProfileBox.IsChecked == true; _settings.Save();
    }

    // ---- per-game profiles ----
    private string CurrentDictionaryFile() =>
        _activeProfile != null ? ProfileStore.DictionaryFile(_activeProfile.ExeName) : UserData.DictionaryFile;

    private void RefreshProfiles()
    {
        _profiles = ProfileStore.LoadAll();
        var keep = ProfileBox.SelectedIndex;
        ProfileBox.ItemsSource = _profiles.Select(p => p.ExeName).ToList();
        ProfileBox.SelectedIndex = _profiles.Count == 0 ? -1 : Math.Clamp(keep, 0, _profiles.Count - 1);
        ProfileText.Text = _activeProfile != null
            ? $"ملف اللعبة الشغال: {_activeProfile.ExeName}"
            : _profiles.Count == 0 ? "مفيش ألعاب محفوظة لسه." : $"{_profiles.Count} لعبة محفوظة.";
    }

    private void RefreshRunning()
    {
        var list = ActiveWindow.RunningWindows();
        _runningExes = list.Select(x => x.Exe).ToList();
        RunningBox.ItemsSource = list.Select(x => $"{x.Exe} — {x.Title}").ToList();
        if (list.Count > 0) RunningBox.SelectedIndex = 0;
    }

    private void RefreshRunning_Click(object sender, RoutedEventArgs e) => RefreshRunning();

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        int idx = RunningBox.SelectedIndex;
        if (idx < 0 || idx >= _runningExes.Count) { ProfileText.Text = "شغّل اللعبة الأول، واضغط «تحديث القايمة»، واختارها."; return; }
        var exe = _runningExes[idx];
        var r = Region;
        var p = new GameProfile
        {
            ExeName = exe, Title = RunningBox.SelectedItem?.ToString() ?? exe,
            X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
            ModeIndex = ModeBox.SelectedIndex, CaptureIndex = CaptureBox.SelectedIndex, MonitorIndex = Math.Max(0, MonitorBox.SelectedIndex),
            SpeedIndex = SpeedBox.SelectedIndex, TextSizeIndex = TextSizeBox.SelectedIndex, PackIndex = PackBox.SelectedIndex,
            Continuous = ContinuousBox.IsChecked == true, Masculine = MasculineBox.IsChecked == true, Precise = PreciseBox.IsChecked == true,
            AutoStart = AutoStartBox.IsChecked == true,
        };
        p.Style.CopyFrom(_settings.Style);
        try
        {
            ProfileStore.Save(p);
            _activeProfile = p;
            RefreshProfiles();
            ProfileText.Text = $"اتحفظت إعدادات {exe}. القاموس والاصطلاحات الخاصة بيها: {Path.GetFileName(ProfileStore.DictionaryFile(exe))}";
        }
        catch (Exception ex) { ProfileText.Text = "فشل الحفظ: " + ex.Message; }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedIndex < 0 || ProfileBox.SelectedIndex >= _profiles.Count) return;
        LoadProfile(_profiles[ProfileBox.SelectedIndex]);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedIndex < 0 || ProfileBox.SelectedIndex >= _profiles.Count) return;
        var p = _profiles[ProfileBox.SelectedIndex];
        ProfileStore.Delete(p.ExeName);
        if (_activeProfile?.ExeName == p.ExeName) _activeProfile = null;
        RefreshProfiles();
    }

    private void LoadProfile(GameProfile p)
    {
        _activeProfile = p;
        Volatile.Write(ref _regionBox, (object)new System.Drawing.Rectangle(p.X, p.Y, p.Width, p.Height));
        if (p.Width >= 20 && p.Height >= 10) RegionText.Text = $"المنطقة: {p.Width}×{p.Height}";
        _settings.X = p.X; _settings.Y = p.Y; _settings.Width = p.Width; _settings.Height = p.Height;
        ModeBox.SelectedIndex = Math.Clamp(p.ModeIndex, 0, 1);
        CaptureBox.SelectedIndex = Math.Clamp(p.CaptureIndex, 0, 1);
        if (_monitors.Count > 0) MonitorBox.SelectedIndex = Math.Clamp(p.MonitorIndex, 0, _monitors.Count - 1);
        SpeedBox.SelectedIndex = Math.Clamp(p.SpeedIndex, 0, 2);
        TextSizeBox.SelectedIndex = Math.Clamp(p.TextSizeIndex, 0, 2);
        PackBox.SelectedIndex = Math.Clamp(p.PackIndex, 0, 1);
        ContinuousBox.IsChecked = p.Continuous; MasculineBox.IsChecked = p.Masculine; PreciseBox.IsChecked = p.Precise;
        AutoStartBox.IsChecked = p.AutoStart;
        _settings.Style.CopyFrom(p.Style);
        _settings.Save();
        ProfileText.Text = $"اتحمّلت إعدادات اللعبة: {p.ExeName}";
    }

    private void WatcherTick()
    {
        if (!_ready || AutoProfileBox.IsChecked != true || _pipeline != null || _screen != null) return;
        var exe = ActiveWindow.ForegroundExe();
        if (exe == null) return;
        var p = _profiles.FirstOrDefault(x => x.ExeName.Equals(exe, StringComparison.OrdinalIgnoreCase));
        if (p == null || _activeProfile?.ExeName == p.ExeName) return;
        LoadProfile(p);
        if (p.AutoStart) StartTranslation();
    }

    // ---- other windows ----
    private void Glossary_Click(object sender, RoutedEventArgs e) => new GlossaryWindow(_settings) { Owner = this }.Show();
    private void History_Click(object sender, RoutedEventArgs e) => new HistoryWindow(CurrentDictionaryFile) { Owner = this }.Show();

    private OverlayWindow EnsureOverlay()
    {
        _overlay ??= new OverlayWindow();
        _overlay.ApplyStyle(_settings.Style);
        return _overlay;
    }

    private ScreenOverlayWindow EnsureScreenWindow() => _screenWindow ??= new ScreenOverlayWindow();

    private System.Drawing.Rectangle PreviewRegion() =>
        Region.Width >= 20 && Region.Height >= 10 ? Region : OverlayWindow.DefaultPreviewRegion();

    private System.Drawing.Rectangle? PickOverlayArea()
    {
        var selector = new RegionSelectorWindow("اسحب بالماوس لتحديد مكان وحجم شريط الترجمة. Esc للإلغاء");
        return selector.ShowDialog() == true ? selector.SelectedRegion : (System.Drawing.Rectangle?)null;
    }

    private void PreviewScreenCards()
    {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        var heading = new ScreenBlock(" ", new System.Drawing.Rectangle(w / 2 - 300, h / 3, 360, 40), BlockKind.Heading, 34);
        var body1 = new ScreenBlock(" ", new System.Drawing.Rectangle(w / 2 - 300, h / 3 + 56, 420, 26), BlockKind.Body, 22);
        var body2 = new ScreenBlock(" ", new System.Drawing.Rectangle(w / 2 - 300, h / 3 + 90, 420, 26), BlockKind.Body, 22);
        var sample = new List<TranslatedBlock>
        {
            new(heading, "المهام المطلوبة"),
            new(body1, "اعثر على المفتاح الذهبي"),
            new(body2, "تحدث مع الحارس عند البوابة"),
        };
        var win = EnsureScreenWindow();
        win.ShowBlocks(sample, new System.Drawing.Rectangle(0, 0, w, h), _settings.Style);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        timer.Tick += (_, _) => { timer.Stop(); if (_screen == null) win.ClearAll(); };
        timer.Start();
    }

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        var overlay = EnsureOverlay();
        var win = new AppearanceWindow(_settings.Style, PickOverlayArea,
            changed: () => { _settings.Save(); overlay.ApplyStyle(_settings.Style); overlay.ShowPreview(PreviewRegion()); },
            preview: () => overlay.ShowPreview(PreviewRegion()),
            previewScreen: PreviewScreenCards)
        { Owner = this };
        win.Closed += (_, _) => { if (_pipeline == null) overlay.Hide(); };
        win.Show();
    }

    private void GameFiles_Click(object sender, RoutedEventArgs e)
    {
        var win = new GameFilesWindow(_settings, () =>
        {
            var (resolver, translator, _) = BuildTranslation(threads: 4, beams: 1);
            return (resolver, translator);
        }) { Owner = this };
        win.Show();
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

    // ---- the translator (shared by region mode, screen mode and game files) ----
    private (TranslationResolver Resolver, ITranslator Translator, string Mode) BuildTranslation(int threads, int beams)
    {
        // chosen pack → (if missing) the other pack → (if none) the temporary online service
        var chosen = PackBox.SelectedIndex == 1 ? LanguagePack.Heavy : LanguagePack.Light;
        var other = chosen.Kind == PackKind.Heavy ? LanguagePack.Light : LanguagePack.Heavy;
        var pack = chosen.IsInstalled() ? chosen : other.IsInstalled() ? other : null;

        ITranslator inner; string mode;
        if (pack != null)
        {
            var backup = pack == chosen ? other : chosen;
            bool backupInstalled = backup.IsInstalled();
            var lazy = new LazyTranslator(
                () => pack.CreateTranslator(threads, beams),
                () => backupInstalled ? backup.CreateTranslator(threads, beams) : new OnlineTranslator("en", "ar"));
            lazy.EngineReady += (eng, err) => ShowEngine(eng, err, pack.ShortName);
            ShowEngine(lazy.Engine, lazy.Error, pack.ShortName);   // in case it finished before we subscribed
            inner = lazy; mode = "بدون إنترنت";
        }
        else
        {
            inner = new OnlineTranslator("en", "ar");
            ShowEngine("online", null, "");
            mode = "بالإنترنت (مؤقتًا)";
        }

        // the user's own masculine.txt is added to the built-in list
        try { _masculineMap = ArabicGender.ParseUserFile(File.ReadAllLines(UserData.MasculineFile)); } catch { }
        var translator = new PostProcessTranslator(inner, s => _masculine ? ArabicGender.MakeMasculine(s, _masculineMap) : s);

        UserData.EnsureTemplates();
        // dictionary priority: the game's own file > the general file > ready glossaries
        var exe = _activeProfile?.ExeName;
        var dictionary = BuiltInDictionary.Load(UserData.DictionaryFile);
        foreach (var kv in Glossaries.Load(_settings.Glossaries)) dictionary.TryAdd(kv.Key, kv.Value);
        if (exe != null) foreach (var kv in BuiltInDictionary.Load(ProfileStore.DictionaryFile(exe))) dictionary[kv.Key] = kv.Value;
        var idioms = IdiomRewriter.LoadMany(exe != null ? ProfileStore.IdiomsFile(exe) : null, UserData.IdiomsFile);

        var resolver = new TranslationResolver(translator, new TranslationCache(), dictionary, idioms, Phrasebook.LoadEmbedded());
        return (resolver, translator, mode);
    }

    private IScreenGrabber MakeGrabber() => CaptureBox.SelectedIndex == 1 ? new DxgiScreenGrabber() : new GdiScreenGrabber();

    // ---- start / stop ----
    private void Start_Click(object sender, RoutedEventArgs e) => StartTranslation();

    private void StartTranslation()
    {
        if (_pipeline != null || _screen != null) return;
        bool screen = ScreenMode;
        if (!screen && (Region.Width < 20 || Region.Height < 10)) { StatusText.Text = "اختر منطقة النص أولًا (الزر الأول)"; return; }

        var ocr = new WindowsOcrEngine("en-US", screen ? 1 : TextScale());
        if (!ocr.IsAvailable)
        {
            StatusText.Text = "حزمة OCR الإنجليزية غير مثبتة: Settings ← Time & language ← Language & region ← أضف English";
            return;
        }

        int threads = SpeedBox.SelectedIndex == 0 ? 1 : 2;   // few CPU threads: the game keeps priority
        int beams = SpeedBox.SelectedIndex != 0 && PreciseBox.IsChecked == true ? 3 : 1;   // beam search only on request
        var (resolver, translator, mode) = BuildTranslation(threads, beams);
        _translator = translator;
        _grabber = MakeGrabber();

        if (screen) StartScreen(ocr, resolver); else StartRegion(ocr, resolver);

        StatusText.Text = (screen ? "ترجمة الشاشة كاملة شغّالة — " : "الترجمة شغّالة — ") + mode;
        StartButton.IsEnabled = false; StopButton.IsEnabled = true; SelectButton.IsEnabled = !screen; ModeBox.IsEnabled = false;
    }

    private void StartRegion(WindowsOcrEngine ocr, TranslationResolver resolver)
    {
        var sink = new WpfOverlaySink(EnsureOverlay(), () => Region);

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
            _lastArabic = ar;
            Dispatcher.InvokeAsync(() => LastText.Text = $"المقروء: {src}\nالترجمة: {ar}");
            AppendHistory(src, ar);
            if (_settings.Speak) _ = SpeakAsync(ar);
        };
        p.Start();
        _pipeline = p;

        _capture = new RegionCaptureWorker(p, _grabber!, () => Region, () => _intervalMs, 1, s => SetStatus(s));
        _capture.Start();
    }

    private void StartScreen(WindowsOcrEngine ocr, TranslationResolver resolver)
    {
        var area = _monitors.Count > 0 && MonitorBox.SelectedIndex >= 0
            ? _monitors[Math.Min(MonitorBox.SelectedIndex, _monitors.Count - 1)].Bounds
            : new System.Drawing.Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
        var overlay = new WpfScreenOverlay(EnsureScreenWindow(), () => _settings.Style);
        var options = new ScreenPipelineOptions
        {
            Continuous = ContinuousBox.IsChecked == true,
            Interval = TimeSpan.FromMilliseconds(SpeedBox.SelectedIndex switch { 0 => 1200, 2 => 450, _ => 700 }),
            Settle = TimeSpan.FromMilliseconds(SpeedBox.SelectedIndex switch { 0 => 800, 2 => 350, _ => 500 }),
        };
        var sp = new ScreenPipeline(_grabber!, ocr, resolver, overlay, () => area, options, s => SetStatus(s));
        sp.Updated += blocks =>
        {
            foreach (var b in blocks)
                if (_loggedBlocks.Add(b.Block.Text)) AppendHistory(b.Block.Text, b.Arabic);     // new texts go to the history for correction
            Dispatcher.InvokeAsync(() => LastText.Text = $"عدد النصوص المترجمة على الشاشة: {blocks.Count}");
        };
        sp.Start();
        _screen = sp;
        if (!options.Continuous) sp.ScanNow();
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
        var c = _capture; var p = _pipeline; var s = _screen; var g = _grabber;
        _capture = null; _pipeline = null; _screen = null; _grabber = null;
        c?.Dispose();
        if (p != null) await p.DisposeAsync();
        if (s != null) await s.DisposeAsync();
        g?.Dispose();
        (_translator as IDisposable)?.Dispose(); _translator = null;
        _speaker?.Stop();
        _overlay?.Hide();
        _screenWindow?.ClearAll();
        _loggedBlocks.Clear();
        EngineText.Text = "";
        LastText.Text = "";
        StatusText.Text = "متوقف";
        StartButton.IsEnabled = true; StopButton.IsEnabled = false; SelectButton.IsEnabled = true; ModeBox.IsEnabled = true;
        UpdatePackStatus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _watcher.Stop();
        if (WindowState == WindowState.Normal && ActualWidth > 0)
        {
            _settings.WindowWidth = ActualWidth; _settings.WindowHeight = ActualHeight; _settings.Save();
        }
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyToggle); UnregisterHotKey(_hwnd, HotkeyScan);
            UnregisterHotKey(_hwnd, HotkeyCards); UnregisterHotKey(_hwnd, HotkeySpeak);
        }
        _capture?.Dispose();
        _grabber?.Dispose();
        _speaker?.Dispose();
        _overlay?.Close();
        _screenWindow?.Close();
        base.OnClosed(e);
    }
}
