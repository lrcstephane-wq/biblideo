using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Biblideo.App.ViewModels;
using Biblideo.Core.Models;

namespace Biblideo.App.Views;

public partial class LicenseAssignmentWindow : Window
{
    public LicenseAssignmentWindow(LicenseInboxRowViewModel row, IReadOnlyList<ClientRecord> clients)
    {
        InitializeComponent();
        FileText.Text = row.FileName;
        var choices = clients
            .OrderBy(client => client.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(client => new ClientChoice(client))
            .ToArray();
        ClientCombo.ItemsSource = choices;
        AddressCombo.ItemsSource = row.Message.Recipients;
        if (row.Message.Recipients.Count > 0) AddressCombo.SelectedIndex = 0;
        if (row.Item.CandidateClients.Count == 1)
            ClientCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Client.MetadataId == row.Item.CandidateClients[0].MetadataId);
        HintText.Text = row.Message.Recipients.Count == 0
            ? "Aucune adresse exploitable n’a été lue. L’association restera ponctuelle."
            : "L’adresse mémorisée permettra de ranger automatiquement les prochains mails. Même le facteur gagne du temps.";
    }

    public ClientRecord? SelectedClient { get; private set; }
    public string? SelectedAddress { get; private set; }
    public bool SaveAddress => SaveAddressCheck.IsChecked == true && !string.IsNullOrWhiteSpace(SelectedAddress);

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedClient = (ClientCombo.SelectedItem as ClientChoice)?.Client;
        SelectedAddress = AddressCombo.SelectedItem?.ToString();
        ConfirmButton.IsEnabled = SelectedClient is not null;
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

    private sealed class ClientChoice(ClientRecord client)
    {
        public ClientRecord Client { get; } = client;
        public override string ToString() => $"{Client.DisplayName} · {Client.Variant.ToFolderToken()}";
    }
}
