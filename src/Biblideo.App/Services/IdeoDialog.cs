using System.Windows;
using Biblideo.App.Views;

namespace Biblideo.App.Services;

public static class IdeoDialog
{
    public static void Info(string title, string message, string? aside = null) =>
        Show(title, message, IdeoDialogKind.Information, aside ?? "Les engrenages ont vérifié deux fois. Par principe.", "Compris", null);

    public static void Warning(string title, string message, string? aside = null) =>
        Show(title, message, IdeoDialogKind.Warning, aside ?? "Petit détour conseillé avant de remettre la machine en route.", "Compris", null);

    public static void Error(string title, string message, string? aside = null) =>
        Show(title, message, IdeoDialogKind.Error, aside ?? "Le composant responsable nie toute implication.", "Compris", null);

    public static bool Confirm(string title, string message, string? aside = null, string primaryLabel = "Continuer", string secondaryLabel = "Annuler") =>
        Show(title, message, IdeoDialogKind.Confirmation, aside ?? "Le bouton Annuler reste une excellente stratégie de sauvegarde.", primaryLabel, secondaryLabel);

    private static bool Show(string title, string message, IdeoDialogKind kind, string? aside, string primaryLabel, string? secondaryLabel)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                    ?? Application.Current?.MainWindow;
        var dialog = new IdeoDialogWindow(title, message, kind, aside, primaryLabel, secondaryLabel);
        if (owner is not null && owner.IsVisible) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
