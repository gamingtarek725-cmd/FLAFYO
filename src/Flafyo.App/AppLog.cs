using System.IO;

namespace Flafyo.Desktop;

internal static class AppLog
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(UserData.Dir);
            File.AppendAllText(UserData.ErrorLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Write(Exception ex) => Write(ex.ToString());
}
