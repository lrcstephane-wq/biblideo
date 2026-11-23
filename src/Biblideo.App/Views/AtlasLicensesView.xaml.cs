using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Services;
using Biblideo.Core.Services;
using Microsoft.Win32;

namespace Biblideo.App.Views;

public partial class AtlasLicensesView : UserControl
{
    private string? _keyPath;
    private bool _busy;
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public AtlasLicensesView()
    {
        InitializeComponent();
        Licenses.SelectionChanged += Selection_Changed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync(string? select = null)
    {
        if (_busy) return;
        await LoadAsync(select);
    }

    private async Task LoadAsync(string? select)
    {
        SetBusy(true);
        try
        {
            var customers = await NexusRegistryClient.LoadCustomersAsync();
            var rights = await NexusRegistryClient.LoadAtlasAsync();
            Customers.ItemsSource = customers.Clients;
            Licenses.ItemsSource = rights;
            Licenses.SelectedItem = rights.FirstOrDefault(right => right.Id == select);
            StatusText.Text = customers.Clients.Length == 0
                ? "Crée d’abord une fiche dans Clients Nexus."
                : "Connecté · Registre Atlas de test";
        }
        catch (Exception error)
        {
            Licenses.ItemsSource = null;
            StatusText.Text = $"Registre Atlas indisponible · {error.Message}";
        }
        finally
        {
            _busy = false;
            Fields.IsEnabled = Actions.IsEnabled = Licenses.ItemsSource is not null;
            UpdateSelectionActions();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Licenses.IsEnabled = !busy;
        Fields.IsEnabled = Actions.IsEnabled = !busy;
    }

    private void UpdateSelectionActions()
    {
        var right = Licenses.SelectedItem as NexusAtlasRight;
        RenewButton.IsEnabled = TransferButton.IsEnabled = ExportButton.IsEnabled = HistoryButton.IsEnabled = right is not null;
        UpgradeButton.IsEnabled = right is { IsCreator: false };
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionActions();
        if (Licenses.SelectedItem is not NexusAtlasRight right) return;
        Customers.SelectedItem = Customers.Items.OfType<NexusCustomer>().FirstOrDefault(customer => customer.Id == right.ClientId);
        DeviceInput.Text = right.DeviceId;
        ExpiryInput.Text = right.ValidUntil.ToString("dd/MM/yyyy", French);
        OfferInput.SelectedIndex = right.IsCreator ? 1 : 0;
    }

    private async void Sign_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !Actions.IsEnabled || sender is not Button { Tag: string operation }) return;
        var current = operation == "create" ? null : Licenses.SelectedItem as NexusAtlasRight;
        if (current is null && operation != "create") return;
        var customer = Customers.SelectedItem as NexusCustomer;
        if (current is null && customer is null)
        {
            StatusText.Text = "Sélectionne un client du registre Nexus.";
            return;
        }
        if (current is not null && customer?.Id != current.ClientId)
        {
            StatusText.Text = "Le client de la licence sélectionnée doit rester identique.";
            return;
        }

        var id = current?.Id ?? Guid.NewGuid().ToString("N");
        try
        {
            var device = operation is "renew" or "upgrade" ? current!.DeviceId : AtlasLicenseCodec.NormalizeDevice(DeviceInput.Text);
            DateOnly expiry;
            if (operation is "transfer" or "upgrade")
                expiry = current!.ValidUntil;
            else if (!DateOnly.TryParseExact(ExpiryInput.Text.Trim(), "dd/MM/yyyy", French, DateTimeStyles.None, out expiry))
                throw new ArgumentException("Renseigne l’échéance au format JJ/MM/AAAA.");
            var creator = operation == "upgrade" || (current?.IsCreator ?? (OfferInput.SelectedIndex == 1));
            if (current is null && expiry < DateOnly.FromDateTime(DateTime.Today))
                throw new ArgumentException("L’échéance d’une nouvelle licence doit être aujourd’hui ou ultérieure.");
            if (current is not null && (operation == "renew" && expiry <= current.ValidUntil
                || operation == "transfer" && device == current.DeviceId))
                throw new ArgumentException("Saisis une date ultérieure pour renouveler ou un autre identifiant pour changer de poste.");

            if (!IdeoDialog.Confirm("Confirmer la licence Atlas",
                $"{customer!.Name}\n{device}\n{(creator ? "Créateur" : "Standard")} · jusqu’au {expiry:dd/MM/yyyy}\n\nLe fichier pourra être exporté après enregistrement.",
                string.Empty, "Signer")) return;
            if (_keyPath is null)
            {
                var picker = new OpenFileDialog { Title = "Clé de signature Atlas conservée sur ce poste", Filter = "Clés PEM|*.pem" };
                if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
                _keyPath = picker.FileName;
            }

            SetBusy(true);
            var key = await File.ReadAllTextAsync(_keyPath);
            var code = AtlasLicenseCodec.Sign(new AtlasLicenseClaims(customer.Name, id, device, expiry, creator), key);
            await NexusRegistryClient.WriteAtlasAsync(id, customer.Id, code, current?.Revision ?? 0, operation);
        }
        catch (Exception error)
        {
            if (error is ArgumentException or CryptographicException or IOException) _keyPath = null;
            IdeoDialog.Warning("Licence Atlas à vérifier", error.Message);
        }
        finally
        {
            SetBusy(false);
        }

        await RefreshAsync(id);
    }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Licenses.SelectedItem is not NexusAtlasRight right) return;
        SetBusy(true);
        try
        {
            var entries = await NexusRegistryClient.LoadAtlasHistoryAsync(right.Id);
            var lines = entries.Take(15).Select(entry =>
            {
                var label = entry.Operation switch
                {
                    "create" => "Création",
                    "renew" => "Renouvellement",
                    "transfer" => "Changement de poste",
                    "upgrade" => "Passage en Créateur",
                    _ => entry.Operation
                };
                return $"{entry.RecordedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {label}\n"
                    + $"{entry.Previous?.DeviceId ?? "—"} → {entry.Current.DeviceId} · {entry.Current.Offer} · {entry.Current.ValidUntil:dd/MM/yyyy}";
            });
            IdeoDialog.Info("Historique Atlas", entries.Length == 0 ? "Aucune opération enregistrée." : string.Join("\n\n", lines));
        }
        catch (Exception error)
        {
            IdeoDialog.Warning("Historique indisponible", error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Licenses.SelectedItem is not NexusAtlasRight right) return;
        var picker = new SaveFileDialog { FileName = "Licence.fautpastoucher", Filter = "Licence Atlas|*.fautpastoucher", OverwritePrompt = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        SetBusy(true);
        try
        {
            var code = await NexusRegistryClient.DownloadAtlasAsync(right.Id);
            var claims = AtlasLicenseCodec.Read(code);
            if (claims.LicenseId != right.Id || claims.DeviceId != right.DeviceId || claims.ValidUntil != right.ValidUntil
                || claims.IsCreator != right.IsCreator)
                throw new InvalidOperationException("La licence a changé depuis l’affichage. Actualise la liste puis réessaie.");
            var path = picker.FileName;
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, code);
                File.Move(temporary, path, true);
            }
            finally
            {
                File.Delete(temporary);
            }
            StatusText.Text = "Fichier Atlas exporté.";
        }
        catch (Exception error)
        {
            IdeoDialog.Warning("Export Atlas impossible", error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
}
