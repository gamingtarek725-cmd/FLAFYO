using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Flafyo.Desktop;

/// <summary>Shows the latest translations and lets the user correct one; the correction goes to the dictionary file.</summary>
public partial class HistoryWindow : Window
{
    public sealed class HistoryItem
    {
        public string Source { get; init; } = "";
        public string Arabic { get; init; } = "";
        public string Display => $"{Source}   ⇒   {Arabic}";
    }

    private readonly Func<string> _dictionaryFile;

    public HistoryWindow(Func<string> dictionaryFile)
    {
        InitializeComponent();
        _dictionaryFile = dictionaryFile;
        Reload();
    }

    private void Reload()
    {
        var items = new List<HistoryItem>();
        try
        {
            if (File.Exists(UserData.HistoryFile))
            {
                foreach (var line in File.ReadAllLines(UserData.HistoryFile).TakeLast(300).Reverse())
                {
                    int idx = line.IndexOf("  =>  ", StringComparison.Ordinal);
                    if (idx < 0) continue;
                    var head = line[..idx];
                    var source = head.Length > 10 ? head[10..] : head;      // "HH:mm:ss  " prefix
                    items.Add(new HistoryItem { Source = source.Trim(), Arabic = line[(idx + 6)..].Trim() });
                }
            }
        }
        catch { }
        List.ItemsSource = items;
        StatusText.Text = items.Count == 0 ? "السجل فاضي. الترجمات بتتسجل هنا أثناء اللعب." : "";
        if (items.Count > 0) List.SelectedIndex = 0;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is HistoryItem item) { SourceBox.Text = item.Source; ArabicBox.Text = item.Arabic; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var source = SourceBox.Text.Trim(); var arabic = ArabicBox.Text.Trim();
        if (source.Length == 0 || arabic.Length == 0) { StatusText.Text = "اختار جملة واكتب الترجمة."; return; }
        if (source.Contains('=')) { StatusText.Text = "الجملة فيها علامة = ومش ينفع تتحفظ في القاموس. ضيفها لملف الاصطلاحات بدلها."; return; }
        try
        {
            var file = _dictionaryFile();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, $"{source} = {arabic}{Environment.NewLine}", System.Text.Encoding.UTF8);   // a later line wins over an earlier one
            StatusText.Text = "اتحفظ. هيشتغل من أول ترجمة جاية (ابدأ الترجمة من جديد لو شغالة).";
        }
        catch (Exception ex) { StatusText.Text = "فشل الحفظ: " + ex.Message; }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
