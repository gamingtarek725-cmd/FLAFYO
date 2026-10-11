using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Flafyo.Core;

namespace Flafyo.Desktop;

public partial class GlossaryWindow : Window
{
    private readonly AppSettings _settings;

    public GlossaryWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        foreach (var pack in Glossaries.All)
        {
            int count = Glossaries.Load(new[] { pack.Id }).Count;
            var box = new CheckBox
            {
                Content = $"{pack.Title}  ({count} مصطلح)",
                Tag = pack.Id,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 4, 0, 4),
                IsChecked = settings.Glossaries.Contains(pack.Id),
            };
            box.Checked += Changed; box.Unchecked += Changed;
            Panel.Children.Add(box);
        }
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        _settings.Glossaries = Panel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
        _settings.Save();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
