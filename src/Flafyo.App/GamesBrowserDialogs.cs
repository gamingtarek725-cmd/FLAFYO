using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Flafyo.Desktop;

internal sealed class GamesBrowserDialog : Window
{
    private readonly Action<string> _openGame;

    internal GamesBrowserDialog(IReadOnlyList<ModsLibraryWindow.GameCard> games, Action<string> openGame)
    {
        _openGame = openGame;
        Title = "مكتبة الألعاب - FLAFYO";
        Width = 850;
        Height = 620;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#0F1420");
        FlowDirection = FlowDirection.RightToLeft;
        Foreground = Brushes.White;

        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = "اختر اللعبة", FontSize = 25, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = "كل بطاقة تعرض عدد مصادر المودات الموجودة حاليًا في الفهرس.",
            Foreground = Brush("#AAB8D0"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var game in games)
        {
            var card = new Border
            {
                Width = 225,
                Height = 255,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(9),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("#30415F"),
                Background = Brush("#172238")
            };
            var stack = new StackPanel();
            var cover = new Border
            {
                Height = 145,
                CornerRadius = new CornerRadius(8),
                Background = Brush("#202E49"),
                ClipToBounds = true
            };
            if (Uri.TryCreate(game.CoverUrl, UriKind.Absolute, out var coverUri) &&
                coverUri.Scheme == Uri.UriSchemeHttps &&
                (coverUri.Host.EndsWith("akamai.steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
                 coverUri.Host.Equals("images.gamebanana.com", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var image = new Image { Stretch = Stretch.UniformToFill };
                    image.Source = new BitmapImage(coverUri);
                    cover.Child = image;
                }
                catch { }
            }
            if (cover.Child == null)
                cover.Child = new TextBlock
                {
                    Text = "FLAFYO GAMES",
                    Foreground = Brush("#7186AE"),
                    FontWeight = FontWeights.Black,
                    FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            stack.Children.Add(cover);
            stack.Children.Add(new TextBlock
            {
                Text = game.Name,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, 10, 2, 4),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{game.ModCount} مصدر مود",
                Foreground = Brush("#8DEBFA"),
                FontSize = 11,
                Margin = new Thickness(2, 0, 2, 8)
            });
            var choose = new Button
            {
                Content = "عرض مودات اللعبة",
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brush("#1CBFD2"),
                Foreground = Brush("#071321"),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };
            choose.Click += (_, _) => { _openGame(game.Name); Close(); };
            stack.Children.Add(choose);
            card.Child = stack;
            wrap.Children.Add(card);
        }
        if (games.Count == 0)
            wrap.Children.Add(new TextBlock
            {
                Text = "لا توجد ألعاب في الفهرس حاليًا. اضغط تحديث المكتبة وحاول مرة أخرى.",
                Foreground = Brush("#D7E2F5"),
                Margin = new Thickness(8)
            });
        scroll.Content = wrap;
        root.Children.Add(scroll);
        Content = root;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}

internal sealed class GameModsDialog : Window
{
    internal GameModsDialog(string gameName, IReadOnlyList<ModsLibraryWindow.ModEntry> mods)
    {
        Title = $"مودات {gameName} - FLAFYO";
        Width = 760;
        Height = 560;
        MinWidth = 560;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1420"));
        Foreground = Brushes.White;
        FlowDirection = FlowDirection.RightToLeft;

        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(new TextBlock { Text = gameName, FontSize = 24, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock
        {
            Text = $"مصادر المودات المتاحة حاليًا: {mods.Count}",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAB8D0")),
            Margin = new Thickness(0, 5, 0, 0)
        });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var list = new StackPanel();
        foreach (var mod in mods)
        {
            var card = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172238")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30415F")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = mod.Title, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            info.Children.Add(new TextBlock
            {
                Text = $"{mod.Category} · {mod.SourceName}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8DEBFA")),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0)
            });
            info.Children.Add(new TextBlock
            {
                Text = mod.Summary,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0D9EA")),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 8, 0)
            });
            grid.Children.Add(info);
            var open = new Button
            {
                Content = "فتح المصدر الأصلي ↗",
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1CBFD2")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#071321")),
                BorderThickness = new Thickness(0),
                IsEnabled = IsAllowedSource(mod.SourceUrl)
            };
            open.Click += (_, _) =>
            {
                if (!IsAllowedSource(mod.SourceUrl)) return;
                try { Process.Start(new ProcessStartInfo(mod.SourceUrl) { UseShellExecute = true }); }
                catch { MessageBox.Show(this, "تعذر فتح المصدر. حاول مرة أخرى.", "FLAFYO", MessageBoxButton.OK, MessageBoxImage.Information); }
            };
            Grid.SetColumn(open, 1);
            grid.Children.Add(open);
            card.Child = grid;
            list.Children.Add(card);
        }
        if (mods.Count == 0)
            list.Children.Add(new TextBlock
            {
                Text = "لا توجد مودات موثقة لهذه اللعبة في الفهرس حتى الآن. ستظهر هنا عند إضافة مصادر متوافقة وموثوقة.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0D9EA")),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            });
        scroll.Content = list;
        root.Children.Add(scroll);
        Content = root;
    }

    private static bool IsAllowedSource(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var allowed = new[] { "curseforge.com", "minecraft.net", "moddingway.com", "fifa-infinity.com", "steamcommunity.com", "farming-simulator.com", "stardewvalleywiki.com" };
        return allowed.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                   uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }
}
