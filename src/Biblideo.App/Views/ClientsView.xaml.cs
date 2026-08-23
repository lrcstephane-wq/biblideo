using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Services;
using Biblideo.App.ViewModels;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public partial class ClientsView : UserControl
{
    private readonly AppSettings _appSettings;
    private readonly ClientModuleContext _settings;
    private readonly ClientManagementService _managementService = new();
    private readonly ClientRegistryService _registryService = new();
    private readonly OutlookNotificationService _outlookService = new();
    private readonly LicenseCatalogService _licenseCatalogService = new();
    private readonly LicenseManagementService _licenseManagementService = new();
    private readonly ObservableCollection<ClientRowViewModel> _visibleRows = [];
    private readonly List<ClientRowViewModel> _allRows = [];
    private ClientRegistry _registry = new();
    private ClientScanResult _lastScan = new();
    private bool _loading;

    public ClientsView(AppSettings appSettings, ClientModuleContext settings)
    {
        _appSettings = appSettings;
        _settings = settings;
        InitializeComponent();
        DataContext = _visibleRows;
        StatusFilter.SelectedIndex = 0;
        VariantFilter.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadClientsAsync();
    }

    private async void ClientsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClientsList.SelectedItem is not ClientRowViewModel row) return;
        ClientDetailsPanel.DataContext = row;
        ClientDetailsPanel.Visibility = Visibility.Visible;
        NoClientEmailText.Visibility = row.Client.Emails.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClientLicenseExpiryText.Text = "Analyse…";
        ClientLicenseStateText.Text = "Lecture des licences archivées";
        ClientHistoryList.ItemsSource = ReadClientHistory(row.Client.DisplayName);
        await LoadClientLicenseSummaryAsync(row.Client);
    }

    private async Task LoadClientLicenseSummaryAsync(ClientRecord client)
    {
        try
        {
            var folder = Path.Combine(client.FullPath, _appSettings.LicenseClientFolderName);
            var paths = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.msg", SearchOption.TopDirectoryOnly).ToArray()
                : [];
            var messages = await _licenseCatalogService.ReadAsync(paths, _appSettings.GetEffectiveLicenseIndexPath());
            var summary = _licenseManagementService.Summarize(client, messages, DateTime.Today, _appSettings.LicenseRenewalWarningDays);
            if (ClientDetailsPanel.DataContext is not ClientRowViewModel current || current.Client.MetadataId != client.MetadataId) return;
            ClientLicenseExpiryText.Text = summary.ExpiryDate?.ToString("dd/MM/yyyy") ?? "Pas de licence";
            ClientLicenseStateText.Text = summary.State switch
            {
                ClientLicenseState.Valid => $"Valide · {summary.DaysRemaining} jours restants",
                ClientLicenseState.RenewSoon => $"À renouveler · {summary.DaysRemaining} jours restants",
                ClientLicenseState.Expired => $"Expirée depuis {-summary.DaysRemaining} jours",
                ClientLicenseState.OffSubscription => "Hors mise à jour",
                ClientLicenseState.Unreadable => "Échéance à vérifier",
                _ => "Aucun mail archivé"
            };
        }
        catch (Exception exception)
        {
            ClientLicenseExpiryText.Text = "Indisponible";
            ClientLicenseStateText.Text = exception.Message;
        }
    }

    private IReadOnlyList<ClientHistoryDisplay> ReadClientHistory(string clientName)
    {
        if (!File.Exists(_settings.ClientHistoryPath)) return [];
        var entries = new List<ClientHistoryDisplay>();
        foreach (var line in File.ReadLines(_settings.ClientHistoryPath).TakeLast(100).Reverse())
        {
            try
            {
                var item = JsonSerializer.Deserialize<ClientHistoryEntry>(line);
                if (item is null || !item.ClientName.Contains(clientName, StringComparison.CurrentCultureIgnoreCase)) continue;
                entries.Add(new ClientHistoryDisplay(
                    item.TimestampUtc.ToLocalTime().ToString("dd/MM HH:mm"),
                    item.Action switch
                    {
                        "create" => "Client créé",
                        "edit" => "Fiche modifiée",
                        "subscription_change" => "Abonnement modifié",
                        "repair_structure" => "Structure réparée",
                        "license_email_added" => "Adresse licence ajoutée",
                        _ => "Opération enregistrée"
                    },
                    item.Details ?? item.After ?? item.Before ?? string.Empty));
                if (entries.Count == 6) break;
            }
            catch { }
        }
        return entries;
    }

    private void CloseClientDetails_Click(object sender, RoutedEventArgs e)
    {
        ClientDetailsPanel.Visibility = Visibility.Collapsed;
        ClientsList.SelectedItem = null;
    }

    private async Task LoadClientsAsync()
    {
        if (_loading) return;
        _loading = true;
        IsEnabled = false;
        ScanSummaryText.Text = "Lecture des dossiers clients…";
        try
        {
            _registry = await _registryService.LoadAsync(_settings.ClientRegistryPath);
            _lastScan = await Task.Run(() => _managementService.Scan(_settings, _registry));
            var registryChanged = ReconcileRegistry(_lastScan.Clients);
            if (registryChanged) await _registryService.SaveAsync(_settings.ClientRegistryPath, _registry);

            foreach (var row in _allRows) row.PropertyChanged -= Row_PropertyChanged;
            _allRows.Clear();
            foreach (var client in _lastScan.Clients)
            {
                var row = new ClientRowViewModel(client);
                row.PropertyChanged += Row_PropertyChanged;
                _allRows.Add(row);
            }
            RefreshFilter();
            UpdateSummary();
        }
        catch (Exception exception)
        {
            IdeoDialog.Error("Clients indisponibles", $"Impossible de lire la racine clients.\n\n{exception.Message}", "Même les dossiers ont parfois besoin d’un café réseau.");
            ScanSummaryText.Text = "Lecture impossible · vérifie la racine clients dans les paramètres";
        }
        finally
        {
            IsEnabled = true;
            _loading = false;
        }
    }

    private bool ReconcileRegistry(IReadOnlyList<ClientRecord> clients)
    {
        var changed = false;
        foreach (var client in clients)
        {
            var canonical = ClientFolderNamingService.BuildCanonicalName(client.BaseName);
            var metadata = _registry.Clients.FirstOrDefault(item => item.Id == client.MetadataId)
                           ?? _registry.Clients.FirstOrDefault(item => !item.IsArchived && item.CanonicalName.Equals(canonical, StringComparison.OrdinalIgnoreCase));
            if (metadata is null)
            {
                _registry.Clients.Add(new ClientMetadata { Id = client.MetadataId, CanonicalName = canonical, FolderName = client.FolderName });
                changed = true;
                continue;
            }
            if (!metadata.FolderName.Equals(client.FolderName, StringComparison.Ordinal) || !metadata.CanonicalName.Equals(canonical, StringComparison.Ordinal))
            {
                metadata.FolderName = client.FolderName;
                metadata.CanonicalName = canonical;
                metadata.UpdatedAtUtc = DateTime.UtcNow;
                changed = true;
            }
        }
        return changed;
    }

    private void RefreshFilter()
    {
        if (!IsInitialized) return;
        var search = SearchText.Text.Trim();
        var status = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var variant = (VariantFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var filtered = _allRows.Where(row =>
            (string.IsNullOrWhiteSpace(search) || row.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) || row.FolderName.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Client.Emails.Any(email => email.Contains(search, StringComparison.OrdinalIgnoreCase))) &&
            (status == "All" || status == "Subscribed" && row.DesiredSubscribed || status == "Unsubscribed" && !row.DesiredSubscribed || status == "Warning" && (!row.Client.StructureIsValid || row.Client.Emails.Count == 0) || status == "Structure" && !row.Client.StructureIsValid || status == "Email" && row.Client.Emails.Count == 0) &&
            (variant == "All" || row.Client.Variant.ToString() == variant));
        _visibleRows.Clear();
        foreach (var row in filtered) _visibleRows.Add(row);
        EmptyText.Visibility = _visibleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePendingBar();
    }

    private void UpdateSummary()
    {
        var warningCount = _allRows.Count(row => !row.Client.StructureIsValid || row.Client.Emails.Count == 0);
        TotalCountText.Text = _allRows.Count.ToString("N0");
        SubscribedCountText.Text = _allRows.Count(row => row.Client.IsSubscribed).ToString("N0");
        UnsubscribedCountText.Text = _allRows.Count(row => !row.Client.IsSubscribed).ToString("N0");
        WarningCountText.Text = warningCount.ToString("N0");
        ScanSummaryText.Text = $"{_allRows.Count:N0} client(s) reconnu(s) · {_lastScan.IgnoredFolders.Count:N0} dossier(s) hors périmètre";
    }

    private void ClientSummary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string status }) return;
        foreach (ComboBoxItem item in StatusFilter.Items)
        {
            if (item.Tag?.ToString() == status) { StatusFilter.SelectedItem = item; break; }
        }
    }

    private void UpdatePendingBar()
    {
        var pending = _allRows.Count(row => row.HasPendingChange);
        PendingBar.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingTitle.Text = pending == 1 ? "1 changement de statut en attente" : $"{pending} changements de statut en attente";
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClientRowViewModel.HasPendingChange) or nameof(ClientRowViewModel.DesiredSubscribed))
        {
            UpdatePendingBar();
            if (((StatusFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All") != "All") RefreshFilter();
        }
    }

    private void SubscriptionToggle_Click(object sender, RoutedEventArgs e) => UpdatePendingBar();
    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshFilter();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadClientsAsync();

    private void CancelPending_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _allRows) row.DesiredSubscribed = row.Client.IsSubscribed;
        RefreshFilter();
    }

    private async void ApplyPending_Click(object sender, RoutedEventArgs e)
    {
        var changes = _allRows.Where(row => row.HasPendingChange).Select(row => new ClientSubscriptionChange(row.Client, row.DesiredSubscribed)).ToArray();
        if (changes.Length == 0) return;
        var activationCount = changes.Count(change => change.NewStatus);
        var deactivationCount = changes.Length - activationCount;
        if (!IdeoDialog.Confirm(
                "Appliquer les statuts de mise à jour",
                $"{changes.Length} dossier(s) vont être renommés.\n\nActivations : {activationCount}\nDésactivations : {deactivationCount}",
                "Le petit tiret bas va travailler. Merci de ne pas le distraire.",
                "Appliquer")) return;

        try
        {
            IsEnabled = false;
            var completed = await _managementService.ApplySubscriptionChangesAsync(_settings, changes);
            foreach (var operation in completed)
            {
                var metadata = FindMetadata(operation.Change.Client);
                metadata.FolderName = Path.GetFileName(operation.NewPath);
                metadata.UpdatedAtUtc = DateTime.UtcNow;
                await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry
                {
                    Action = "subscription_change",
                    ClientName = operation.Change.Client.DisplayName,
                    Before = operation.Change.Client.IsSubscribed ? "Avec MAJ" : "Sans MAJ",
                    After = operation.Change.NewStatus ? "Avec MAJ" : "Sans MAJ",
                    Details = $"{operation.OldPath} -> {operation.NewPath}"
                });
            }
            await _registryService.SaveAsync(_settings.ClientRegistryPath, _registry);
            try
            {
                _outlookService.SendSubscriptionChanges(changes, _settings.ClientNotificationRecipients);
            }
            catch (Exception mailException)
            {
                await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry { Action = "notification_failed", ClientName = string.Join(", ", changes.Select(change => change.Client.DisplayName)), Details = mailException.Message });
                IdeoDialog.Warning("Statuts appliqués · mail non envoyé", $"Les dossiers ont bien été renommés, mais Outlook n’a pas envoyé la notification.\n\n{mailException.Message}", "Les clients sont à jour. Outlook, lui, fait une petite pause syndicale.");
            }
            await LoadClientsAsync();
        }
        catch (Exception exception)
        {
            IdeoDialog.Error("Modification interrompue", $"Aucun changement incomplet ne doit être considéré comme validé.\n\n{exception.Message}");
            await LoadClientsAsync();
        }
        finally { IsEnabled = true; }
    }

    private async void NewClient_Click(object sender, RoutedEventArgs e)
    {
        var editor = new ClientEditorWindow(_settings.ClientSubscriptionMarker) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() != true || editor.Draft is not { } draft) return;
        var canonical = ClientFolderNamingService.BuildCanonicalName(draft.BaseName);
        if (_registry.Clients.Any(item => !item.IsArchived && item.CanonicalName.Equals(canonical, StringComparison.OrdinalIgnoreCase)))
        {
            IdeoDialog.Warning("Client déjà connu", "Un client portant ce nom existe déjà. Utilise Modifier pour éviter les jumeaux numériques.");
            return;
        }
        var folderName = ClientFolderNamingService.BuildFolderName(draft.BaseName, draft.Variant, draft.IsSubscribed, _settings.ClientSubscriptionMarker);
        if (!IdeoDialog.Confirm("Créer le client", $"Le dossier {folderName} et sa structure seront créés dans la racine clients.", "Cinq dossiers bien rangés. Marie Kondo approuve probablement.", "Créer")) return;
        try
        {
            var path = _managementService.CreateClient(_settings, draft);
            var metadata = new ClientMetadata { CanonicalName = canonical, FolderName = Path.GetFileName(path), Emails = draft.Emails.ToList(), UpdatedAtUtc = DateTime.UtcNow };
            _registry.Clients.Add(metadata);
            await _registryService.SaveAsync(_settings.ClientRegistryPath, _registry);
            await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry { Action = "create", ClientName = folderName, After = path, Details = string.Join(";", draft.Emails) });
            await LoadClientsAsync();
        }
        catch (Exception exception) { IdeoDialog.Error("Création impossible", exception.Message); }
    }

    private async void EditClient_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientRowViewModel row }) return;
        var editor = new ClientEditorWindow(_settings.ClientSubscriptionMarker, row.Client) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() != true || editor.Draft is not { } draft) return;
        var canonical = ClientFolderNamingService.BuildCanonicalName(draft.BaseName);
        if (_registry.Clients.Any(item => item.Id != row.Client.MetadataId && !item.IsArchived && item.CanonicalName.Equals(canonical, StringComparison.OrdinalIgnoreCase)))
        {
            IdeoDialog.Warning("Nom déjà utilisé", "Un autre client porte déjà ce nom.");
            return;
        }
        var newFolderName = ClientFolderNamingService.BuildFolderName(draft.BaseName, draft.Variant, draft.IsSubscribed, _settings.ClientSubscriptionMarker);
        if (!IdeoDialog.Confirm("Modifier le client", $"Le dossier deviendra :\n{newFolderName}\n\nLes adresses mail associées seront également actualisées.", "On change l’étiquette, pas le contenu de la boîte.", "Enregistrer")) return;
        try
        {
            var oldFolder = row.Client.FolderName;
            var targetPath = _managementService.UpdateClientFolder(_settings, row.Client, draft);
            var metadata = FindMetadata(row.Client);
            metadata.CanonicalName = canonical;
            metadata.FolderName = Path.GetFileName(targetPath);
            metadata.Emails = draft.Emails.ToList();
            metadata.UpdatedAtUtc = DateTime.UtcNow;
            await _registryService.SaveAsync(_settings.ClientRegistryPath, _registry);
            await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry { Action = "edit", ClientName = row.Client.DisplayName, Before = oldFolder, After = metadata.FolderName });
            if (row.Client.IsSubscribed != draft.IsSubscribed)
            {
                var change = new ClientSubscriptionChange(row.Client, draft.IsSubscribed);
                try { _outlookService.SendSubscriptionChanges([change], _settings.ClientNotificationRecipients); }
                catch (Exception mailException) { IdeoDialog.Warning("Client modifié · mail non envoyé", mailException.Message); }
            }
            await LoadClientsAsync();
        }
        catch (Exception exception) { IdeoDialog.Error("Modification impossible", exception.Message); }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientRowViewModel row } || row.Client.StructureIsValid) return;
        if (!IdeoDialog.Confirm("Réparer la structure", $"Les {row.Client.MissingFolders.Count} dossier(s) manquant(s) seront créés. Aucun fichier existant ne sera modifié.", "Une réparation qui ne nécessite même pas de marteau.", "Réparer")) return;
        try
        {
            var created = _managementService.RepairStructure(_settings, row.Client);
            await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry { Action = "repair_structure", ClientName = row.Client.DisplayName, Details = string.Join(";", created) });
            await LoadClientsAsync();
        }
        catch (Exception exception) { IdeoDialog.Error("Réparation impossible", exception.Message); }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientRowViewModel row }) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{row.Client.FullPath}\"") { UseShellExecute = true }); }
        catch (Exception exception) { IdeoDialog.Warning("Ouverture impossible", exception.Message); }
    }

    private async void RemoveClient_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientRowViewModel row }) return;
        try
        {
            var inventory = await Task.Run(() => _managementService.InspectFolder(_settings, row.Client));
            var dialog = new ClientRemovalWindow(row.Client, inventory) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            var metadata = FindMetadata(row.Client);
            if (dialog.Choice == ClientRemovalChoice.Archive)
            {
                var archivePath = _managementService.ArchiveClient(_settings, row.Client);
                metadata.IsArchived = true;
                metadata.ArchivedPath = archivePath;
                metadata.UpdatedAtUtc = DateTime.UtcNow;
                await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry { Action = "archive", ClientName = row.Client.DisplayName, Before = row.Client.FullPath, After = archivePath });
            }
            else if (dialog.Choice == ClientRemovalChoice.PermanentDelete)
            {
                _managementService.PermanentlyDeleteEmptyClient(_settings, row.Client);
                _registry.Clients.Remove(metadata);
                await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry { Action = "permanent_delete_empty", ClientName = row.Client.DisplayName, Before = row.Client.FullPath });
            }
            await _registryService.SaveAsync(_settings.ClientRegistryPath, _registry);
            await LoadClientsAsync();
        }
        catch (Exception exception) { IdeoDialog.Error("Retrait impossible", exception.Message, "Le dossier s’accroche. C’est touchant, mais peu pratique."); }
    }

    private void Archives_Click(object sender, RoutedEventArgs e)
    {
        var window = new ClientArchivesWindow(_settings, _registry, _registryService, _managementService) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        if (window.HasRestoredClient) _ = LoadClientsAsync();
    }

    private ClientMetadata FindMetadata(ClientRecord client)
    {
        var canonical = ClientFolderNamingService.BuildCanonicalName(client.BaseName);
        var metadata = _registry.Clients.FirstOrDefault(item => item.Id == client.MetadataId)
                       ?? _registry.Clients.FirstOrDefault(item => !item.IsArchived && item.CanonicalName.Equals(canonical, StringComparison.OrdinalIgnoreCase));
        if (metadata is not null) return metadata;
        metadata = new ClientMetadata { Id = client.MetadataId, CanonicalName = canonical, FolderName = client.FolderName, Emails = client.Emails.ToList() };
        _registry.Clients.Add(metadata);
        return metadata;
    }

    private sealed record ClientHistoryDisplay(string When, string Title, string Detail);
}
