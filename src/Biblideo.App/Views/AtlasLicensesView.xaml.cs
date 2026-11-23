using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Services;
using Biblideo.Core.Services;
using Biblideo.Core.Models;
using Microsoft.Win32;

namespace Biblideo.App.Views;

public partial class AtlasLicensesView : UserControl
{
    private readonly ClientModuleContext _clients;
    private string? _keyPath;

    private static string AtlasKeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions", "Atlas", "Secrets", "Atlas-Licence-ClePrivee.pem");
    private bool _busy;
    private bool _canAdminister;
    private bool _canChangeRegistry;
    private NexusAtlasRight? _selectedRight;

    public AtlasLicensesView(ClientModuleContext clients)
    {
        _clients = clients;
        InitializeComponent();
        if (File.Exists(AtlasKeyPath)) _keyPath = AtlasKeyPath;
        UpdateKeyStatus();
        ExpiryInput.SelectedDate = DateTime.Today.AddYears(1);
        OperatorInput.TextChanged += ClearAdminResponse;
        AdminRequestInput.TextChanged += ClearAdminResponse;
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
        _canAdminister = _canChangeRegistry = false;
        SelectRight(null);
        RegistryCards.ItemsSource = null;
        RegistryEmptyText.Visibility = Visibility.Visible;
        RegistryEmptyText.Text = "Registre Atlas indisponible.";
        try
        {
            var location = NexusRegistryClient.Location;
            NexusCustomer[] customers = [];
            string? directoryError = null;
            try
            {
                customers = await BiblideoClientDirectory.ReadAsync(_clients);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                directoryError = error.Message;
            }

            Customers.ItemsSource = customers;
            if (location.ActiveRoot is null)
            {
                RegistryEmptyText.Text = "Renseigne l’emplacement du registre pour afficher les fiches Atlas.";
                StatusText.Text = "Choisis l’emplacement du registre pour enregistrer des licences de poste.";
                return;
            }

            var rights = await NexusRegistryClient.LoadAtlasAsync();
            _canChangeRegistry = !location.UseCloud;
            _canAdminister = _canChangeRegistry && directoryError is null;
            var namesById = customers.ToDictionary(customer => customer.Id, customer => customer.Name);
            RegistryCards.ItemsSource = rights.GroupBy(right => right.ClientId)
                .Select(company => new AtlasCompanyCard(company.Key,
                    namesById.GetValueOrDefault(company.Key) ?? company.First().Customer,
                    directoryError is null && !namesById.ContainsKey(company.Key),
                    company.ToArray(), _canChangeRegistry))
                .OrderBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            RegistryEmptyText.Text = "Aucune licence Atlas enregistrée.";
            RegistryEmptyText.Visibility = rights.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectRight(rights.FirstOrDefault(right => right.Id == select));
            StatusText.Text = directoryError is not null ? $"Fiches clients indisponibles · {directoryError}"
                : location.UseCloud ? "Copie cloud · Lecture seule"
                : customers.Length == 0 ? "Crée d’abord une fiche dans Clients."
                : "Registre Atlas · Les bénéficiaires viennent des fiches Clients.";
        }
        catch (Exception error)
        {
            _canAdminister = _canChangeRegistry = false;
            StatusText.Text = $"Registre Atlas indisponible · {error.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RegistryCards.IsEnabled = !busy;
        Fields.IsEnabled = !busy && _canAdminister;
        Actions.IsEnabled = !busy;
        UpdateSelectionActions();
    }

    private void UpdateSelectionActions()
    {
        var right = _selectedRight;
        var linked = right is not null && Customers.Items.OfType<NexusCustomer>().Any(client => client.Id == right.ClientId);
        CreateButton.IsEnabled = _canAdminister && Customers.Items.Count != 0;
        RenewButton.IsEnabled = TransferButton.IsEnabled = _canAdminister && linked;
        ExportButton.IsEnabled = CopyButton.IsEnabled = _canAdminister && right is not null;
        UpgradeButton.IsEnabled = _canAdminister && linked && right is { IsCreator: false };
        LinkButton.IsEnabled = _canAdminister && right is not null && !linked && Customers.Items.Count != 0;
    }

    private void SelectRight(NexusAtlasRight? right)
    {
        GeneratedLicenseText.Clear();
        _selectedRight = right;
        UpdateSelectionActions();
        if (right is null) return;
        Customers.SelectedItem = Customers.Items.OfType<NexusCustomer>().FirstOrDefault(customer => customer.Id == right.ClientId);
        DeviceInput.Text = right.DeviceId;
        ExpiryInput.SelectedDate = right.ValidUntil.ToDateTime(TimeOnly.MinValue);
        OfferInput.SelectedIndex = right.IsCreator ? 1 : 0;
    }

    private async void Sign_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canAdminister || !Actions.IsEnabled || sender is not Button { Tag: string operation }) return;
        var current = operation == "create" ? null : _selectedRight;
        if (current is null && operation != "create") return;
        var customer = Customers.SelectedItem as NexusCustomer;
        if (customer is null)
        {
            StatusText.Text = "Sélectionne une fiche dans la liste des clients.";
            return;
        }
        if (current is not null && operation != "link" && customer.Id != current.ClientId)
        {
            StatusText.Text = "Le client de la licence sélectionnée doit rester identique.";
            return;
        }

        var id = current?.Id ?? Guid.NewGuid().ToString("N");
        try
        {
            var device = operation is "renew" or "upgrade" or "link" ? current!.DeviceId : AtlasLicenseCodec.NormalizeDevice(DeviceInput.Text);
            DateOnly expiry;
            if (operation is "transfer" or "upgrade" or "link")
                expiry = current!.ValidUntil;
            else if (ExpiryInput.SelectedDate is { } selectedDate)
                expiry = DateOnly.FromDateTime(selectedDate);
            else
                throw new ArgumentException("Choisis la date de fin de validité.");
            var creator = operation == "upgrade" || (current?.IsCreator ?? (OfferInput.SelectedIndex == 1));
            if (current is null && expiry < DateOnly.FromDateTime(DateTime.Today))
                throw new ArgumentException("L’échéance d’une nouvelle licence doit être aujourd’hui ou ultérieure.");
            if (current is not null && (operation == "renew" && expiry <= current.ValidUntil
                || operation == "transfer" && device == current.DeviceId))
                throw new ArgumentException("Saisis une date ultérieure pour renouveler ou un autre identifiant pour changer de poste.");

            var beneficiary = operation == "link" ? $"{current!.Customer} → {customer.Name}" : customer.Name;
            if (!IdeoDialog.Confirm("Confirmer la licence Atlas",
                $"{beneficiary}\n{device}\n{(creator ? "Créateur" : "Standard")} · jusqu’au {expiry:dd/MM/yyyy}\n\nLe fichier pourra être exporté après enregistrement.",
                string.Empty, "Signer")) return;
            if (_keyPath is null && !PickKey()) return;

            SetBusy(true);
            var key = await File.ReadAllTextAsync(_keyPath!);
            var code = AtlasLicenseCodec.Sign(new AtlasLicenseClaims(customer.Name, id, device, expiry, creator), key);
            await NexusRegistryClient.WriteAtlasAsync(_clients, id, customer, code, current?.Revision ?? 0, operation);
            UpdateKeyStatus();
            _busy = false;
            await RefreshAsync(id);
            GeneratedLicenseText.Text = code;
            StatusText.Text = $"Code prêt pour {customer.Name} · jusqu’au {expiry:dd/MM/yyyy} inclus.";
        }
        catch (Exception error)
        {
            if (error is CryptographicException or IOException)
            {
                _keyPath = null;
                UpdateKeyStatus();
            }
            IdeoDialog.Warning("Licence Atlas à vérifier", error.Message);
        }
        finally
        {
            SetBusy(false);
        }

    }

    private void UpdateKeyStatus() => KeyStatusText.Text = _keyPath is null
        ? "Clé de signature Idéo à sélectionner" : "Clé Idéo locale sélectionnée";

    private bool PickKey()
    {
        var picker = new OpenFileDialog { Title = "Clé de signature Atlas conservée sur ce poste", Filter = "Clés PEM|*.pem" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return false;
        _keyPath = picker.FileName;
        UpdateKeyStatus();
        return true;
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e) => PickKey();

    private async void Configure_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (new NexusConnectionWindow { Owner = Window.GetWindow(this) }.ShowDialog() == true)
            await RefreshAsync();
    }

    private void OpenGenerator_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: NexusAtlasRight right }) return;
        SelectRight(right);
        LicenseTabs.SelectedItem = GeneratorTab;
    }

    private async void RemoveLicense_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NexusAtlasRight right })
            await RemoveAsync(right.ClientId, [right], right.DeviceId, entireCompany: false);
    }

    private async void RemoveCompanyLicenses_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AtlasCompanyCard company })
            await RemoveAsync(company.Id, company.Licenses, company.Name, entireCompany: true);
    }

    private async Task RemoveAsync(Guid clientId, NexusAtlasRight[] displayedRights, string label, bool entireCompany)
    {
        if (_busy || !_canChangeRegistry) return;

        var title = entireCompany ? "Supprimer toutes les licences Atlas" : "Supprimer la licence Atlas";
        var scope = entireCompany
            ? $"{label} · {displayedRights.Length} licence(s) Atlas seront supprimées du registre."
            : $"La licence du poste {label} sera supprimée du registre.";
        if (!IdeoDialog.Confirm(title, scope, "Un code déjà installé dans Atlas reste valable jusqu’à son échéance.",
                primaryLabel: "Supprimer")) return;

        SetBusy(true);
        try
        {
            await NexusRegistryClient.RemoveAtlasAsync(clientId, displayedRights, entireCompany);
            var select = displayedRights.Any(right => right.Id == _selectedRight?.Id) ? null : _selectedRight?.Id;
            await LoadAsync(select);
            StatusText.Text = $"Registre Atlas · {displayedRights.Length} licence(s) supprimée(s).";
        }
        catch (Exception error)
        {
            IdeoDialog.Warning("Suppression Atlas impossible", error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearAdminResponse(object sender, TextChangedEventArgs e)
    {
        AdminResponseText.Clear();
        CopyAdminButton.IsEnabled = false;
    }

    private async void GenerateAdminResponse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        AdminResponseText.Clear();
        CopyAdminButton.IsEnabled = false;
        if (_keyPath is null && !PickKey()) return;

        var request = AdminRequestInput.Text;
        var operatorName = OperatorInput.Text;
        OperatorInput.IsReadOnly = AdminRequestInput.IsReadOnly = true;
        SetBusy(true);
        try
        {
            var key = await File.ReadAllTextAsync(_keyPath!);
            AdminResponseText.Text = AtlasLicenseCodec.SignAdminResponse(request, operatorName, key);
            CopyAdminButton.IsEnabled = true;
        }
        catch (Exception error)
        {
            IdeoDialog.Warning("Réponse Administrateur Idéo non générée", error.Message);
        }
        finally
        {
            OperatorInput.IsReadOnly = AdminRequestInput.IsReadOnly = false;
            SetBusy(false);
        }
    }

    private void CopyAdminResponse_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AdminResponseText.Text)) return;
        try
        {
            Clipboard.SetText(AdminResponseText.Text);
            StatusText.Text = "Réponse Idéo copiée.";
        }
        catch (System.Runtime.InteropServices.ExternalException error)
        {
            IdeoDialog.Warning("Copie de la réponse impossible", error.Message);
        }
    }

    private async void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canAdminister || _selectedRight is not { } right) return;
        SetBusy(true);
        try
        {
            var code = await ReadVerifiedCodeAsync(right);
            GeneratedLicenseText.Text = code;
            Clipboard.SetText(code);
            StatusText.Text = "Code Atlas copié.";
        }
        catch (Exception error)
        {
            IdeoDialog.Warning("Copie du code Atlas impossible", error.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: NexusAtlasRight right }) return;
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
                    "link" => "Rattachement à une fiche client",
                    "remove" => "Suppression du registre",
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

    private static async Task<string> ReadVerifiedCodeAsync(NexusAtlasRight right)
    {
        var code = await NexusRegistryClient.DownloadAtlasAsync(right.Id);
        var claims = AtlasLicenseCodec.Read(code);
        if (claims.LicenseId != right.Id || claims.DeviceId != right.DeviceId || claims.ValidUntil != right.ValidUntil
            || claims.IsCreator != right.IsCreator)
            throw new InvalidOperationException("La licence a changé depuis l’affichage. Actualise la liste puis réessaie.");
        return code;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_canAdminister || _selectedRight is not { } right) return;
        var picker = new SaveFileDialog { FileName = "Licence.fautpastoucher", Filter = "Licence Atlas|*.fautpastoucher", OverwritePrompt = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        SetBusy(true);
        try
        {
            var code = await ReadVerifiedCodeAsync(right);
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
    private sealed record AtlasCompanyCard(Guid Id, string Name, bool NeedsLink, NexusAtlasRight[] Licenses, bool CanDelete)
    {
        public string Summary => $"{Licenses.Length} poste(s) · {Licenses.Count(right => right.IsActiveOn(DateOnly.FromDateTime(DateTime.Today)))} licence(s) active(s)";
    }
}
