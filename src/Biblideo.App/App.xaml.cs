using System.Windows;
using System.Windows.Threading;
using Biblideo.App.Services;

namespace Biblideo.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = TryWriteLog("Erreur d’interface non gérée", e.Exception);
        MessageBox.Show(
            $"BibliDéo a rencontré une erreur, mais reste ouvert.\n\nLe détail a été enregistré ici :\n{logPath}",
            "Erreur BibliDéo",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryWriteLog("Erreur fatale", exception);
        }
    }

    private static string TryWriteLog(string context, Exception exception)
    {
        try
        {
            return CrashLogService.Write(context, exception);
        }
        catch
        {
            return "Journal local indisponible";
        }
    }
}
