using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Services;
using Biblideo.Core.Models;

namespace Biblideo.App.Views;

public partial class NexusCustomersView : UserControl
{
    private readonly ClientModuleContext _biblideo;
    private bool _busy;

    public NexusCustomersView(ClientModuleContext biblideo)
    {
        _biblideo = biblideo;
        InitializeComponent();
        ImportButton.Visibility = NexusPreviewProfile.Enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        SetEditing(false);
        try
        {
            var snapshot = await NexusRegistryClient.LoadCustomersAsync();
            Customers.ItemsSource = snapshot.Clients;
            StatusText.Text = "Connecté · Registre Nexus de test";
            SetEditing(true);
        }
        catch (Exception error) when (error is System.Net.Http.HttpRequestException or OperationCanceledException)
        {
            var cached = await NexusRegistryClient.ReadCachedCustomersAsync();
            Customers.ItemsSource = cached?.Clients;
            StatusText.Text = cached is null
                ? "Service inaccessible · Aucune copie locale disponible"
                : $"Lecture seule · Copie du {cached.GeneratedAtUtc.ToLocalTime():dd/MM/yyyy à HH:mm}";
        }
        catch (Exception error)
        {
            StatusText.Text = $"Registre indisponible · {error.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetEditing(bool online)
    {
        NewButton.IsEnabled = SaveButton.IsEnabled = ImportButton.IsEnabled = online;
        NameInput.IsReadOnly = !online;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !ImportButton.IsEnabled || NexusPreviewProfile.Enabled) return;
        _busy = true;
        SetEditing(false);
        try
        {
            var (records, skipped) = await BiblideoCustomerImport.ReadAsync(_biblideo);
            var existing = Customers.Items.OfType<NexusCustomer>().Select(customer => customer.Id).ToHashSet();
            var additions = records.Count(record => !existing.Contains(record.Id));
            if (additions == 0)
            {
                IdeoDialog.Info("Import Biblidéo", $"Aucune nouvelle fiche à importer. Fiches archivées ou incomplètes ignorées : {skipped}.");
                return;
            }
            if (!IdeoDialog.Confirm("Importer les fiches Biblidéo",
                $"{additions} nouvelle(s) fiche(s) détectée(s). Fiches archivées ou incomplètes ignorées : {skipped}.\n\n"
                + "Seuls les noms et identifiants sont copiés. Les données et les dossiers Biblidéo restent à leur emplacement.",
                primaryLabel: "Importer")) return;

            var result = await NexusRegistryClient.ImportCustomersAsync(records);
            IdeoDialog.Info("Import Biblidéo", $"{result.Added} fiche(s) importée(s) · {result.Existing} déjà présente(s).");
        }
        catch (Exception error)
        {
            IdeoDialog.Warning("Import Biblidéo à vérifier", error.Message);
        }
        finally
        {
            _busy = false;
            await RefreshAsync();
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        Customers.SelectedItem = null;
        NameInput.Clear();
        NameInput.Focus();
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) =>
        NameInput.Text = (Customers.SelectedItem as NexusCustomer)?.Name ?? string.Empty;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        if (_busy || name.Length == 0) return;
        _busy = true;
        SetEditing(false);
        string? failure = null;
        try
        {
            await NexusRegistryClient.SaveCustomerAsync(Customers.SelectedItem as NexusCustomer, name);
        }
        catch (Exception error)
        {
            failure = error.Message;
        }
        finally
        {
            _busy = false;
        }

        await RefreshAsync();
        if (failure is not null)
            IdeoDialog.Warning("Enregistrement à vérifier", failure);
    }
}
