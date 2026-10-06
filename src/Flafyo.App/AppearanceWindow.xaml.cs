using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flafyo.Desktop;

/// <summary>Controls for the look and place of the translation strip. Every change is applied and previewed immediately.</summary>
public partial class AppearanceWindow : Window
{
    private static readonly (string Name, string Hex)[] TextColors =
    {
        ("أبيض", "#FFFFFF"), ("أصفر", "#FFE14D"), ("ذهبي", "#FFC857"), ("برتقالي", "#FF9F43"), ("أحمر فاتح", "#FF6B6B"),
        ("وردي", "#FF8AD8"), ("سماوي", "#4DE1FF"), ("أزرق فاتح", "#7AB8FF"), ("أخضر", "#5CFF9D"), ("ليموني", "#C6FF4D"),
        ("رمادي فاتح", "#D0D4E0"), ("أسود", "#000000"), ("أخضر الشعار", "#00D9A5"),
    };

    private static readonly (string Name, string Hex)[] BackColors =
    {
        ("أسود", "#000000"), ("كحلي غامق", "#101018"), ("رمادي غامق", "#2B2D36"), ("أزرق غامق", "#0B1F3A"),
        ("أحمر غامق", "#3A0B0B"), ("أخضر غامق", "#0B3A1E"), ("بنفسجي غامق", "#25103A"), ("أبيض", "#FFFFFF"),
    };

    private readonly OverlayStyle _s;
    private readonly Func<System.Drawing.Rectangle?> _pickArea;
    private readonly Action _changed, _preview;
    private bool _loading = true;

    public AppearanceWindow(OverlayStyle style, Func<System.Drawing.Rectangle?> pickArea, Action changed, Action preview)
    {
        InitializeComponent();
        _s = style; _pickArea = pickArea; _changed = changed; _preview = preview;
        FillFonts();
        FillColors(TextColorBox, TextColors);
        FillColors(BackColorBox, BackColors);
        FillColors(BorderColorBox, TextColors);
        LoadFromStyle();
        _loading = false;
        Loaded += (_, _) => _preview();
    }

    private static SolidColorBrush Brush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); } catch { return Brushes.White; }
    }

    private static ComboBoxItem ColorItem(string name, string hex) => new()
    {
        Tag = hex,
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Border { Width = 18, Height = 18, Background = Brush(hex), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) },
                new TextBlock { Text = name, Margin = new Thickness(8, 0, 0, 0) },
            }
        }
    };

    private static void FillColors(ComboBox box, (string Name, string Hex)[] colors)
    {
        foreach (var (n, h) in colors) box.Items.Add(ColorItem(n, h));
    }

    private static void SelectColor(ComboBox box, string hex)
    {
        foreach (ComboBoxItem it in box.Items)
            if (string.Equals(it.Tag as string, hex, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = it; return; }
        var custom = ColorItem(hex, hex);
        box.Items.Add(custom); box.SelectedItem = custom;
    }

    private static string ColorOf(ComboBox box, string fallback) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    private void FillFonts()
    {
        var installed = new HashSet<string>(Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);
        var curated = new[] { "Segoe UI", "Tahoma", "Arial", "Traditional Arabic", "Simplified Arabic", "Sakkal Majalla",
                              "Arabic Typesetting", "Times New Roman", "Verdana", "Calibri", "Cambria", "Courier New", "Trebuchet MS" };
        foreach (var f in curated) if (installed.Contains(f)) FontBox.Items.Add(f);
        foreach (var f in installed.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            if (!FontBox.Items.Contains(f)) FontBox.Items.Add(f);
    }

    private void LoadFromStyle()
    {
        if (!FontBox.Items.Contains(_s.FontFamily)) FontBox.Items.Insert(0, _s.FontFamily);
        FontBox.SelectedItem = _s.FontFamily;
        SizeSlider.Value = _s.FontSize;
        BoldCheck.IsChecked = _s.Bold;
        ShadowCheck.IsChecked = _s.Shadow;
        AlignBox.SelectedIndex = Math.Clamp(_s.Align, 0, 2);
        SelectColor(TextColorBox, _s.TextColor);
        SelectColor(BackColorBox, _s.BackColor);
        SelectColor(BorderColorBox, _s.BorderColor);
        OpacitySlider.Value = _s.BackOpacity;
        BorderCheck.IsChecked = _s.Border;
        ModeBox.SelectedIndex = Math.Clamp(_s.OverlayMode, 0, 2);
        WidthSlider.Value = _s.AutoWidthPx;
        HideSlider.Value = _s.AutoHideSeconds;
        OriginalCheck.IsChecked = _s.ShowOriginal;
        UpdateLabels();
    }

    private void ReadToStyle()
    {
        _s.FontFamily = FontBox.SelectedItem as string ?? _s.FontFamily;
        _s.FontSize = SizeSlider.Value;
        _s.Bold = BoldCheck.IsChecked == true;
        _s.Shadow = ShadowCheck.IsChecked == true;
        _s.Align = Math.Max(0, AlignBox.SelectedIndex);
        _s.TextColor = ColorOf(TextColorBox, _s.TextColor);
        _s.BackColor = ColorOf(BackColorBox, _s.BackColor);
        _s.BorderColor = ColorOf(BorderColorBox, _s.BorderColor);
        _s.BackOpacity = (int)OpacitySlider.Value;
        _s.Border = BorderCheck.IsChecked == true;
        _s.OverlayMode = Math.Max(0, ModeBox.SelectedIndex);
        _s.AutoWidthPx = (int)WidthSlider.Value;
        _s.AutoHideSeconds = (int)HideSlider.Value;
        _s.ShowOriginal = OriginalCheck.IsChecked == true;
    }

    private void UpdateLabels()
    {
        SizeLabel.Text = $"{SizeSlider.Value:0}";
        OpacityLabel.Text = $"{OpacitySlider.Value:0}%";
        WidthLabel.Text = WidthSlider.Value < 1 ? "نفس عرض منطقة النص" : $"{WidthSlider.Value:0} بكسل";
        HideLabel.Text = HideSlider.Value < 1 ? "لا تختفي" : $"{HideSlider.Value:0} ثانية";
        AreaText.Text = _s.AreaW >= 100 ? $"المكان المحدد: {_s.AreaW}×{_s.AreaH} بكسل" : "لم يتم تحديد مكان بعد";
        AreaButton.IsEnabled = true;
    }

    private void Apply()
    {
        if (_loading) return;
        ReadToStyle();
        UpdateLabels();
        _changed();
    }

    private void OnSelect(object sender, SelectionChangedEventArgs e) => Apply();
    private void OnToggle(object sender, RoutedEventArgs e) => Apply();
    private void OnSlide(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();

    private void Area_Click(object sender, RoutedEventArgs e)
    {
        var r = _pickArea();
        if (r is not { } rect || rect.Width < 100) return;
        _s.AreaX = rect.X; _s.AreaY = rect.Y; _s.AreaW = rect.Width; _s.AreaH = rect.Height;
        _loading = true; ModeBox.SelectedIndex = 2; _loading = false;
        Apply();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => _preview();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        _s.CopyFrom(new OverlayStyle());
        LoadFromStyle();
        _loading = false;
        _changed();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
