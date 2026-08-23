using System.Windows;
using System.Windows.Input;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public enum ClientRemovalChoice { None, Archive, PermanentDelete }

public partial class ClientRemovalWindow : Window
{
    private readonly string _confirmationName;
    public ClientRemovalChoice Choice { get; private set; }

    public ClientRemovalWindow(ClientRecord client, ClientFolderInventory inventory)
    {
        InitializeComponent();
        _confirmationName = client.DisplayName;
        ClientText.Text = client.DisplayName;
        InventoryText.Text = inventory.IsEmpty
            ? "Le dossier ne contient aucun fichier. La suppression définitive est donc disponible."
            : $"{inventory.FileCount:N0} fichier(s) · {FormatBytes(inventory.TotalBytes)} · archivage récupérable uniquement.";
        ConfirmationText.IsEnabled = inventory.IsEmpty;
        DeleteButton.IsEnabled = false;
        DeleteExplanation.Text = inventory.IsEmpty
            ? $"Pour déverrouiller ce choix, saisis exactement : {_confirmationName}"
            : "Indisponible : le dossier contient des fichiers. Pas de bouton rouge héroïque aujourd’hui.";
    }

    private void Archive_Click(object sender, RoutedEventArgs e) { Choice = ClientRemovalChoice.Archive; DialogResult = true; }
    private void Delete_Click(object sender, RoutedEventArgs e) { Choice = ClientRemovalChoice.PermanentDelete; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Choice = ClientRemovalChoice.None; DialogResult = false; }
    private void ConfirmationText_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        DeleteButton.IsEnabled = ConfirmationText.Text.Equals(_confirmationName, StringComparison.CurrentCulture);
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
