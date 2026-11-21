using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Flafyo.Desktop;

public partial class MainWindow
{
    private static readonly HttpClient UpdateHttp = CreateUpdateHttpClient();

    private static HttpClient CreateUpdateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FLAFYO-Game-Translator/0.3.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "جارٍ التحقق...";
        try
        {
            using var response = await UpdateHttp.GetAsync(
                "https://api.github.com/repos/gamingtarek725-cmd/FLAFYO/releases/latest");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                UpdateStatusText.Text = "لا توجد نسخة منشورة للتحديث بعد.";
                return;
            }
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var versionText = (tag ?? string.Empty).TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var latest))
            {
                UpdateStatusText.Text = "تعذر قراءة رقم إصدار التحديث.";
                return;
            }

            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            if (latest <= current)
            {
                UpdateStatusText.Text = $"أنت تستخدم أحدث إصدار ({current.ToString(3)}).";
                return;
            }

            string? installerUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (string.Equals(name, "FLAFYO-GAME-TRANSLATOR-Setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = url;
                        break;
                    }
                }
            }

            if (!Uri.TryCreate(installerUrl, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps ||
                !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusText.Text = $"الإصدار {latest} متاح، لكن ملف التثبيت غير موجود في صفحة الإصدار.";
                return;
            }

            UpdateStatusText.Text = $"الإصدار {latest} متاح — جارٍ تنزيل المُثبّت...";
            var bytes = await UpdateHttp.GetByteArrayAsync(downloadUri);
            if (bytes.Length < 1024 * 1024)
                throw new InvalidDataException("حجم ملف التحديث غير متوقع؛ تم إلغاء التثبيت.");

            var installerPath = Path.Combine(Path.GetTempPath(), "FLAFYO-GAME-TRANSLATOR-Update.exe");
            await File.WriteAllBytesAsync(installerPath, bytes);
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            UpdateStatusText.Text = "تم فتح مُثبّت التحديث. أكمل خطوات التثبيت.";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "تعذر التحديث: " + ex.Message;
        }
        finally
        {
            if (IsLoaded) CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void DashboardLive_Click(object sender, RoutedEventArgs e) => Start_Click(sender, e);
    private void DashboardFiles_Click(object sender, RoutedEventArgs e) => GameFiles_Click(sender, e);
    private void DashboardLanguages_Click(object sender, RoutedEventArgs e) => SettingsTabs.SelectedIndex = 0;
    private void DashboardGameSettings_Click(object sender, RoutedEventArgs e) => SettingsTabs.SelectedIndex = 4;
}
