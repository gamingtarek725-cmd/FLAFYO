using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Flafyo.Desktop;

public partial class ModsLibraryWindow : UserControl
{
    public sealed class ModEntry
    {
        public string Title { get; init; } = "";
        public string Game { get; init; } = "";
        public string Platform { get; init; } = "";
        public string Category { get; init; } = "";
        public string SourceName { get; init; } = "";
        public string SourceUrl { get; init; } = "";
        public string Version { get; init; } = "";
        public string Summary { get; init; } = "";
        public string InstallMethod { get; init; } = "";
        public string SafetyNotes { get; init; } = "";
        public string CoverUrl { get; init; } = "";
    }

    private readonly ObservableCollection<ModEntry> _visible = new();
    private readonly System.Collections.Generic.List<ModEntry> _remoteCatalog = new();
    private bool _refreshing;
    private string _platformFilter = "all";

    public ModsLibraryWindow()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _visible;
        ApplyCatalog(ModsCatalogService.LoadCache());
        RefreshList();
        Loaded += async (_, _) => await RefreshCatalogAsync();
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e) => await RefreshCatalogAsync();

    private async Task RefreshCatalogAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        UpdateStatus.Text = "جارٍ تحديث فهرس مصادر المودات...";
        try
        {
            var catalog = await ModsCatalogService.FetchAsync();
            ApplyCatalog(catalog);
            UpdateStatus.Text = DateTimeOffset.TryParse(catalog.GeneratedAt, out var stamp)
                ? "آخر تحديث للمكتبة: " + stamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : "تم تحديث المكتبة بنجاح.";
        }
        catch
        {
            UpdateStatus.Text = _remoteCatalog.Count > 0
                ? "تعذر الاتصال الآن؛ يتم عرض آخر نسخة محفوظة."
                : "تعذر تحميل الفهرس. تحقق من الإنترنت ثم اضغط تحديث المكتبة.";
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = true;
            RefreshList();
        }
    }

    private void ApplyCatalog(ModsCatalogService.Catalog? catalog)
    {
        if (catalog == null) return;
        var entries = catalog.Entries.Select(x => new ModEntry
        {
            Title = x.Title,
            Game = x.Game,
            Platform = x.Platform,
            Category = x.Category,
            SourceName = x.SourceName,
            SourceUrl = x.SourceUrl,
            Version = x.Version,
            Summary = x.Summary,
            InstallMethod = x.InstallMethod,
            SafetyNotes = x.SafetyNotes,
            CoverUrl = IsSafeCoverUrl(x.CoverUrl) ? x.CoverUrl : ""
        }).ToList();
        _remoteCatalog.Clear();
        _remoteCatalog.AddRange(entries);
        RefreshList();
    }

    private void RefreshList()
    {
        var query = SearchBox?.Text?.Trim() ?? "";
        var filtered = _remoteCatalog.Where(x =>
            (_platformFilter == "all" ||
             (_platformFilter == "pc" && HasPlatform(x, "PC", "Windows")) ||
             (_platformFilter == "android" && HasPlatform(x, "Android")) ||
             (_platformFilter == "ios" && HasPlatform(x, "iOS", "iPhone", "iPad")) ||
             (_platformFilter == "multi" && (HasPlatform(x, "متعدد") || HasPlatform(x, "Multi")))) &&
            (string.IsNullOrWhiteSpace(query) ||
             ContainsText(x.Title, query) || ContainsText(x.Game, query) ||
             ContainsText(x.Category, query) || ContainsText(x.SourceName, query) ||
             ContainsText(x.Summary, query) || ContainsText(x.Platform, query)))
            .OrderBy(x => x.Platform, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase);

        var selected = EntriesList.SelectedItem as ModEntry;
        _visible.Clear();
        foreach (var item in filtered) _visible.Add(item);
        if (selected != null)
            EntriesList.SelectedItem = _visible.FirstOrDefault(x =>
                x.SourceUrl.Equals(selected.SourceUrl, StringComparison.OrdinalIgnoreCase));
        if (EntriesList.SelectedItem == null && _visible.Count > 0) EntriesList.SelectedIndex = 0;
        if (CountText is not null) CountText.Text = $"{_visible.Count} مصدر";
        if (_visible.Count == 0) ShowEmpty();
    }

    private void ShowEmpty()
    {
        DetailTitle.Text = "لا توجد نتائج";
        DetailSubtitle.Text = "جرّب تغيير المنصة أو كلمات البحث.";
        DetailMetadata.Text = "";
        DetailInfo.Text = "";
        InstallSteps.Text = "";
        SafetyNotes.Text = "";
        SourceAddress.Text = "";
        OpenSourceButton.IsEnabled = false;
    }

    private static bool IsSafeCoverUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.Equals("cdn.akamai.steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("images.gamebanana.com", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsText(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool HasPlatform(ModEntry entry, params string[] platforms) =>
        platforms.Any(platform => entry.Platform.Contains(platform, StringComparison.OrdinalIgnoreCase));

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void Platform_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button selected) return;
        _platformFilter = selected.Tag?.ToString() ?? "all";
        foreach (var button in PlatformFilters.Children.OfType<Button>())
        {
            bool active = ReferenceEquals(button, selected);
            button.Background = new System.Windows.Media.SolidColorBrush(active
                ? System.Windows.Media.Color.FromRgb(32, 191, 208)
                : System.Windows.Media.Color.FromRgb(23, 35, 58));
            button.Foreground = new System.Windows.Media.SolidColorBrush(active
                ? System.Windows.Media.Color.FromRgb(7, 19, 33)
                : System.Windows.Media.Color.FromRgb(220, 230, 245));
            button.BorderBrush = new System.Windows.Media.SolidColorBrush(active
                ? System.Windows.Media.Color.FromRgb(32, 191, 208)
                : System.Windows.Media.Color.FromRgb(43, 59, 90));
        }
        RefreshList();
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesList.SelectedItem is not ModEntry item) return;
        DetailTitle.Text = item.Title;
        DetailSubtitle.Text = item.SourceName + " · " + item.Category;
        DetailMetadata.Text = string.Join(Environment.NewLine, new[]
        {
            "اللعبة: " + (string.IsNullOrWhiteSpace(item.Game) ? "غير محددة" : item.Game),
            "المنصة: " + (string.IsNullOrWhiteSpace(item.Platform) ? "غير محددة" : item.Platform),
            "الإصدار: " + (string.IsNullOrWhiteSpace(item.Version) ? "راجع صفحة المصدر" : item.Version)
        });
        DetailInfo.Text = item.Summary;
        InstallSteps.Text = item.InstallMethod;
        SafetyNotes.Text = item.SafetyNotes;
        SourceAddress.Text = item.SourceUrl;
        OpenSourceButton.IsEnabled = IsAllowedSource(item.SourceUrl);
    }

    private static bool IsAllowedSource(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var allowed = new[] {
            "nexusmods.com", "moddb.com", "thunderstore.io", "curseforge.com",
            "modrinth.com", "gamebanana.com", "mcpedl.com", "minecraft.net",
            "apps.apple.com", "play.google.com", "github.com"
        };
        return allowed.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                   uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not ModEntry item || !IsAllowedSource(item.SourceUrl)) return;
        try { Process.Start(new ProcessStartInfo(item.SourceUrl) { UseShellExecute = true }); }
        catch { UpdateStatus.Text = "تعذر فتح المصدر. انسخ الرابط وافتحه يدويًا."; }
    }
}
