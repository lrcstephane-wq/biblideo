using System.Windows;
using System.Windows.Threading;
using Biblideo.App.Services;

namespace Biblideo.App;

public partial class App : Application
{
    private static int _errorDialogShown;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (Interlocked.Exchange(ref _errorDialogShown, 1) != 0) return;

        var logPath = TryWriteLog("Erreur d’interface non gérée", e.Exception);
        CrashLogService.TryOpen(logPath);
        try
        {
            IdeoDialog.Error(
                "Erreur BibliDéo",
                $"BibliDéo a rencontré une erreur. Le rapport vient d’être ouvert dans le Bloc-notes.\n\nUne seule alerte sera affichée pour éviter une boucle.\n\nEmplacement :\n{logPath}",
                "Le rapport technique parle couramment développeur. Je m’occupe de la traduction.");
        }
        catch
        {
            // Ultime filet de sécurité si l’erreur survient avant le chargement des ressources graphiques.
            MessageBox.Show(
                $"BibliDéo a rencontré une erreur. Le rapport vient d’être ouvert dans le Bloc-notes.\n\nEmplacement :\n{logPath}",
                "Erreur BibliDéo",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
