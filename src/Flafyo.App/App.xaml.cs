using System.Threading.Tasks;
using System.Windows;

namespace Flafyo.Desktop;

public partial class App : Application
{
    public App()
    {
        // an unexpected error must never close the program in the middle of a game: it is written to error.log instead
        DispatcherUnhandledException += (_, e) => { AppLog.Write(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write(e.ExceptionObject?.ToString() ?? "unknown error");
        TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Write(e.Exception); e.SetObserved(); };
    }
}
