using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Flafyo.Desktop;

public partial class LocalizationLibraryWindow : Window
{
    public sealed class LibraryEntry
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string KindLabel { get; init; } = "";
        public string Category { get; init; } = "";
        public string Platform { get; init; } = "";
        public string GameVersion { get; init; } = "";
        public string TranslationVersion { get; init; } = "";
        public string PublishedAt { get; init; } = "";
        public string Info { get; init; } = "";
        public string Install { get; init; } = "";
        public string Notes { get; init; } = "";
        public string SourceUrl { get; init; } = "";
        public string SourceName { get; init; } = "";
        public bool IsDirectory { get; init; }
    }

    private readonly ObservableCollection<LibraryEntry> _visible = new();
    private readonly System.Collections.Generic.List<LibraryEntry> _remoteCatalog = new();
    private bool _refreshing;
    private string _categoryFilter = "all";
    private readonly LibraryEntry[] _catalog =
    {
        new()
        {
            Title = "Resident Evil 4 (2005)",
            Subtitle = "PC · الإصدار المذكور في صفحة المصدر: v1.0",
            Platform = "PC",
            GameVersion = "v1.0",
            KindLabel = "تعريب موثق · مصدر خارجي",
            Category = "تعريبات موثقة",
            Info = "صفحة تعريب منشورة على Ta3reebat. الصفحة تذكر أن التعريب مجاني، وحجمه المنشور 880.90 MB، وتنسب العمل إلى IBRAHIM ALOBAIDI. تحقق من إصدار لعبتك قبل التنزيل.",
            Install = "بحسب صفحة المصدر: 1) نزّل الملف وفك ضغطه. 2) أغلق اللعبة. 3) شغّل أداة التثبيت واضغط تثبيت. 4) إذا لم يعثر المثبّت على اللعبة، اختر مجلدها يدويًا.",
            Notes = "المعلومات وطريقة التثبيت من صفحة المصدر نفسها. لا تشغّل المثبّت إلا بعد التأكد من المصدر وفحص الملف، وأنشئ نسخة احتياطية من ملفات اللعبة.",
            SourceUrl = "https://ta3reebat.com/translations/resident-evil-4-2005/",
            SourceName = "Ta3reebat"
        },
        new()
        {
            Title = "Resident Evil 4 Ultimate HD Edition",
            Subtitle = "PC · عدة مشاريع تعريب مدرجة",
            Platform = "PC",
            KindLabel = "دليل تعريبات · روابط أصحاب الأعمال",
            Category = "تعريبات موثقة",
            Info = "يعرض دليل Muarrab أكثر من مشروع لهذه النسخة، مع أسماء المترجمين والمنصات ونوع الترجمة. تختلف الملفات وطريقة التثبيت بين المشاريع.",
            Install = "افتح صفحة اللعبة، واختر مشروع التعريب المناسب، ثم اتبع تعليمات فريق التعريب في صفحة التحميل. لا تستخدم تعليمات مشروع مختلف.",
            Notes = "Muarrab يوجّه إلى صفحات التحميل التابعة لأصحاب التعريبات ولا يعيد رفع الملفات. راجع توافق إصدار اللعبة قبل التثبيت.",
            SourceUrl = "https://muarrab.com/patches/resident-evil-4-ultimate-hd-edition/",
            SourceName = "Muarrab"
        },
        new()
        {
            Title = "Muarrab — دليل شامل للتعريبات",
            Subtitle = "آلاف الألعاب · رسمي ومجتمعي · منصات متعددة",
            Platform = "منصات متعددة",
            KindLabel = "مكتبة خارجية",
            Category = "مكتبات خارجية",
            Info = "دليل يجمع التعريبات الرسمية وتعريبات المجتمع، ويعرض المنصات ونوع الترجمة وروابط أصحاب التعريبات.",
            Install = "ابحث عن اسم لعبتك داخل الدليل. افتح صفحة اللعبة، ثم قسم المنصات والتحميل، واتبع تعليمات فريق التعريب المرتبط.",
            Notes = "توفر التعريب لا يعني أن كل إصدار أو منصة مدعومة. تعريبات المجتمع تختلف جودتها ومصدرها.",
            SourceUrl = "https://muarrab.com/",
            SourceName = "Muarrab",
            IsDirectory = true
        },
        new()
        {
            Title = "Ta3reb — مكتبة التعريبات",
            Subtitle = "PC · PlayStation · Nintendo Switch وغيرها",
            Platform = "PC، PlayStation، Nintendo Switch",
            KindLabel = "مكتبة خارجية",
            Category = "مكتبات خارجية",
            Info = "مكتبة عربية تعرض تعريبات ألعاب متعددة حسب المنصة، ومنها مشاريع للحاسب وأجهزة أخرى.",
            Install = "اختر اللعبة والمنصة داخل الموقع، وافتح صفحة التعريب. اتبع الملاحظات الخاصة برقم الإصدار أو Title ID إن وجدت.",
            Notes = "لا تفترض أن ملف منصة يعمل على منصة أخرى. تحقق من رقم الإصدار والمنصة قبل أي تثبيت.",
            SourceUrl = "https://ta3reb.com/",
            SourceName = "Ta3reb",
            IsDirectory = true
        },
        new()
        {
            Title = "Ta3reebat — تعريبات ألعاب الكمبيوتر",
            Subtitle = "صفحات ألعاب مع خطوات تثبيت",
            KindLabel = "مكتبة خارجية",
            Category = "مكتبات خارجية",
            Info = "موقع يعرض تعريبات PC وصفحات مستقلة للألعاب مع تفاصيل الإصدار وخطوات التثبيت عند توفرها.",
            Install = "ابحث عن اللعبة، واقرأ صفحة التعريب كاملة، ثم استخدم رابط المصدر والتعليمات المنشورة للعبة نفسها.",
            Notes = "قد تتضمن بعض الصفحات أدوات تثبيت. افحص الملفات ولا تتجاوز تحذيرات نظام الحماية لمجرد ظهورها في صفحة التحميل.",
            SourceUrl = "https://ta3reebat.com/",
            SourceName = "Ta3reebat",
            IsDirectory = true
        },
        new()
        {
            Title = "أبو يزن — تعريبات PC وSteam",
            Subtitle = "تعريبات مع صفحات تثبيت وإزالة",
            Platform = "PC، Steam",
            KindLabel = "مكتبة خارجية",
            Category = "مكتبات خارجية",
            Info = "موقع يصف تعريبات PC وSteam، ويذكر أن صفحات الألعاب تتضمن تفاصيل التعريب وأداة التثبيت وخيارات الإزالة.",
            Install = "افتح صفحة اللعبة المحددة واتبع دليلها. تحقق من النسخة المدعومة، وخذ نسخة احتياطية إذا كانت طريقة التثبيت تستبدل ملفات.",
            Notes = "استخدم صفحة اللعبة ذاتها لتأكيد التوافق، ولا تفترض أن المثبّت يدعم كل نسخ المتاجر.",
            SourceUrl = "https://www.keys25.com/",
            SourceName = "أبو يزن",
            IsDirectory = true
        },
        new()
        {
            Title = "Matrix Arabic — أداة بناء التعريب",
            Subtitle = "Windows 10/11 · يعتمد على ملفات اللعبة الأصلية",
            Platform = "Windows 10/11",
            KindLabel = "مكتبة خارجية",
            Category = "مكتبات خارجية",
            Info = "مشروع يذكر أنه يبني ملفات التعريب محليًا على جهاز المستخدم بدل توزيع ملفات اللعبة نفسها. راجع قائمة الألعاب المدعومة داخل موقعه.",
            Install = "راجع متطلبات الموقع، وثبّت الأداة من المصدر الرسمي فقط، ثم اتبع تعليمات اللعبة المدعومة داخل الأداة.",
            Notes = "يتطلب نسخة لعبة أصلية مثبتة بحسب وصف المشروع. لا تحمّل الأداة من مواقع إعادة رفع غير رسمية.",
            SourceUrl = "https://matrixarabic.com/",
            SourceName = "Matrix Arabic",
            IsDirectory = true
        },
        new()
        {
            Title = "Asmar — مكتبة تعريبات",
            Subtitle = "دليل ألعاب مع تعليمات التثبيت",
            KindLabel = "مكتبة خارجية",
            Category = "مكتبات خارجية",
            Info = "مكتبة تعرض صفحات تعريبات وأرقام إصدارات وتعليمات تثبيت بحسب اللعبة.",
            Install = "اختر اللعبة من المكتبة، ثم اقرأ الخطوات والمتطلبات الخاصة بها. بعض التعريبات تتطلب إبقاء لغة اللعبة على الإنجليزية.",
            Notes = "تختلف طريقة التثبيت بين الألعاب؛ لا تطبق خطوات لعبة على لعبة أخرى.",
            SourceUrl = "https://asmar-ar.com/",
            SourceName = "Asmar",
            IsDirectory = true
        }
    };

    public LocalizationLibraryWindow()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _visible;
        RefreshList();
        if (_visible.Count > 0) EntriesList.SelectedIndex = 0;
        ApplyCatalog(LocalizationCatalogService.LoadCache());
        Loaded += async (_, _) => await RefreshCatalogAsync();
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e) => await RefreshCatalogAsync();

    private async Task RefreshCatalogAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        UpdateStatus.Text = "جارٍ فحص مصادر التعريبات...";
        try
        {
            var catalog = await LocalizationCatalogService.FetchAsync();
            ApplyCatalog(catalog);
            UpdateStatus.Text = DateTimeOffset.TryParse(catalog.GeneratedAt, out var stamp)
                ? "آخر تحديث للمكتبة: " + stamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : "تم تحديث المكتبة بنجاح.";
        }
        catch
        {
            UpdateStatus.Text = _remoteCatalog.Count > 0
                ? "تعذر الاتصال الآن؛ يتم عرض آخر نسخة محفوظة."
                : "تعذر الاتصال بالمكتبة؛ تحقق من الإنترنت وحاول مرة أخرى.";
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = true;
            RefreshList();
        }
    }

    private void ApplyCatalog(LocalizationCatalogService.Catalog? catalog)
    {
        if (catalog == null) return;
        var entries = catalog.Entries.Select(x => new LibraryEntry
        {
            Title = x.Title,
            Subtitle = string.Join(" · ", new[] {
                string.IsNullOrWhiteSpace(x.Platform) ? InferPlatforms(x.Summary) : x.Platform,
                x.GameVersion,
                string.IsNullOrWhiteSpace(x.TranslationVersion) ? null : "تعريب " + x.TranslationVersion,
                string.IsNullOrWhiteSpace(x.PublishedAt) ? null : x.PublishedAt }
                .Where(v => !string.IsNullOrWhiteSpace(v))),
            KindLabel = (x.UpdateType == "new" ? "تعريب جديد" : x.UpdateType == "update" ? "تحديث تعريب" : "صفحة تعريب") + " · " + x.SourceName,
            Category = "تعريبات موثقة",
            Platform = string.IsNullOrWhiteSpace(x.Platform) ? InferPlatforms(x.Summary) : x.Platform,
            GameVersion = x.GameVersion,
            TranslationVersion = x.TranslationVersion,
            PublishedAt = x.PublishedAt,
            Info = string.IsNullOrWhiteSpace(x.Summary) ? "تم اكتشاف صفحة تعريب من المصدر." : x.Summary,
            Install = "تعليمات التثبيت التفصيلية تُراجع في صفحة المصدر؛ تختلف الطريقة حسب اللعبة وإصدارها.",
            Notes = "اكتُشف هذا الإدخال آليًا. لم يختبر FLAFYO التوافق أو ملف التعريب. راجع صفحة المصدر قبل التثبيت.",
            SourceUrl = x.SourceUrl,
            SourceName = x.SourceName,
            IsDirectory = false
        }).ToList();
        _remoteCatalog.Clear();
        _remoteCatalog.AddRange(entries);
        RefreshList();
    }

    private void RefreshList()
    {
        var query = SearchBox?.Text?.Trim() ?? "";
        var filtered = _catalog.Concat(_remoteCatalog).Where(x =>
            (_categoryFilter == "all" ||
             (_categoryFilter == "pc" && HasPlatform(x, "PC", "Steam", "Windows")) ||
             (_categoryFilter == "console" && HasPlatform(x, "PS4", "PS5", "PlayStation", "Xbox", "Nintendo", "Switch")) ||
             (_categoryFilter == "mobile" && HasPlatform(x, "Android", "iOS", "iPhone", "iPad")) ||
             (_categoryFilter == "new" && (x.KindLabel.Contains("جديد") || x.KindLabel.Contains("تحديث"))) ||
             (_categoryFilter == "directories" && x.IsDirectory)) &&
            (string.IsNullOrWhiteSpace(query) ||
             ContainsText(x.Title, query) ||
             ContainsText(x.Subtitle, query) ||
             ContainsText(x.SourceName, query) ||
             ContainsText(x.Info, query) ||
             ContainsText(x.KindLabel, query) ||
             ContainsText(x.Platform, query)));
        var entriesList = EntriesList;
        if (entriesList is null) return;

        var selected = entriesList.SelectedItem as LibraryEntry;
        _visible.Clear();
        foreach (var item in filtered) _visible.Add(item);
        if (selected != null) entriesList.SelectedItem = _visible.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(selected.SourceUrl) &&
            x.SourceUrl.Equals(selected.SourceUrl, StringComparison.OrdinalIgnoreCase));
        if (entriesList.SelectedItem == null && _visible.Count > 0) entriesList.SelectedIndex = 0;
        if (CountText is not null) CountText.Text = $"{_visible.Count} عنصر";
        if (_visible.Count == 0) ShowEmpty();
    }

    private static bool ContainsText(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool HasPlatform(LibraryEntry entry, params string[] platforms)
    {
        var source = string.Join(" ", entry.Platform, entry.Subtitle, entry.Info);
        return platforms.Any(platform => System.Text.RegularExpressions.Regex.IsMatch(
            source,
            @"(?<![A-Za-z0-9])" + System.Text.RegularExpressions.Regex.Escape(platform) + @"(?![A-Za-z0-9])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private static string InferPlatforms(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return "";
        var known = new[] { "PC", "PS4", "PS5", "PlayStation", "Xbox", "Nintendo Switch", "Switch", "Android", "iOS", "iPhone", "iPad" };
        return string.Join(", ", known.Where(platform =>
            System.Text.RegularExpressions.Regex.IsMatch(
                summary,
                @"(?<![A-Za-z0-9])" + System.Text.RegularExpressions.Regex.Escape(platform) + @"(?![A-Za-z0-9])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button selected) return;
        _categoryFilter = selected.Tag?.ToString() ?? "all";
        foreach (var button in CategoryFilters.Children.OfType<Button>())
        {
            bool active = ReferenceEquals(button, selected);
            button.Background = new System.Windows.Media.SolidColorBrush(active
                ? System.Windows.Media.Color.FromRgb(32, 191, 208)
                : System.Windows.Media.Color.FromRgb(27, 41, 66));
            button.Foreground = active
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(7, 19, 33))
                : System.Windows.Media.Brushes.White;
        }
        RefreshList();
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesList.SelectedItem is not LibraryEntry item) return;
        DetailTitle.Text = item.Title;
        DetailSubtitle.Text = item.KindLabel;
        DetailMetadata.Text = string.Join(Environment.NewLine, new[]
        {
            string.IsNullOrWhiteSpace(item.Platform) ? null : "المنصة: " + item.Platform,
            string.IsNullOrWhiteSpace(item.GameVersion) ? null : "إصدار اللعبة: " + item.GameVersion,
            string.IsNullOrWhiteSpace(item.TranslationVersion) ? null : "إصدار التعريب: " + item.TranslationVersion,
            string.IsNullOrWhiteSpace(item.PublishedAt) ? null : "تاريخ النشر/التحديث: " + item.PublishedAt
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        DetailInfo.Text = item.Info;
        InstallSteps.Text = "طريقة التثبيت: " + item.Install;
        SafetyNotes.Text = item.Notes;
        SourceAddress.Text = "المصدر: " + item.SourceName + " — " + item.SourceUrl;
        OpenSourceButton.IsEnabled = Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    private void ShowEmpty()
    {
        DetailTitle.Text = "مفيش نتائج مطابقة";
        DetailSubtitle.Text = "جرّب اسم لعبة أو اختار «كل المصادر».";
        DetailMetadata.Text = "";
        DetailInfo.Text = "لا توجد عناصر تطابق البحث أو التصنيف الحالي.";
        InstallSteps.Text = "";
        SafetyNotes.Text = "";
        SourceAddress.Text = "";
        OpenSourceButton.IsEnabled = false;
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not LibraryEntry item ||
            !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "تعذّر فتح صفحة المصدر: " + ex.Message, "FLAFYO", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}