using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Biblideo.App.Services;
using Biblideo.App.ViewModels;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public partial class LicensesView : UserControl
{
    private readonly AppSettings _settings;
    private readonly ClientModuleContext _clientSettings;
    private readonly ClientRegistryService _registryService = new();
    private readonly ClientManagementService _clientService = new();
    private readonly LicenseManagementService _licenseService = new();
    private readonly LicenseCatalogService _catalogService = new();
    private readonly UiPreferencesService _uiPreferencesService = new();
    private readonly UiPreferences _uiPreferences;
    private readonly List<LicenseInboxRowViewModel> _allInboxRows = [];
    private readonly List<ClientLicenseRowViewModel> _allTrackingRows = [];
    private ClientRegistry _registry = new();
    private IReadOnlyList<ClientRecord> _clients = [];
    private LicenseInboxRowViewModel? _selectedInboxRow;
    private ClientLicenseRowViewModel? _selectedTrackingRow;
    private bool _loading;

    public LicensesView(AppSettings settings, ClientModuleContext clientSettings)
    {
        _settings = settings;
        _clientSettings = clientSettings;
        _uiPreferences = _uiPreferencesService.Load();
        InitializeComponent();
        DataContext = this;
        TrackingStatusFilter.SelectedIndex = 0;
        ApplyTrackingView(_uiPreferences.LicenseTrackingUseCardView, persist: false);
        Loaded += async (_, _) => await LoadAsync();
    }

    public ObservableCollection<LicenseInboxRowViewModel> InboxRows { get; } = [];
    public ObservableCollection<ClientLicenseRowViewModel> TrackingRows { get; } = [];

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        IsEnabled = false;
        ScanSummaryText.Text = "Lecture de la boîte et des dossiers clients…";
        try
        {
            _registry = await _registryService.LoadAsync(_clientSettings.ClientRegistryPath);
            var scan = await Task.Run(() => _clientService.Scan(_clientSettings, _registry));
            _clients = scan.Clients;
            var inboxPath = _settings.GetEffectiveLicenseInboxPath();
            var inboxPaths = Directory.Exists(inboxPath)
                ? Directory.EnumerateFiles(inboxPath, "*.msg", SearchOption.TopDirectoryOnly).ToArray()
                : [];
            var clientPathOwners = new Dictionary<string, ClientRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var client in _clients)
            {
                var folder = Path.Combine(client.FullPath, _settings.LicenseClientFolderName);
                if (!Directory.Exists(folder)) continue;
                foreach (var path in Directory.EnumerateFiles(folder, "*.msg", SearchOption.TopDirectoryOnly))
                    clientPathOwners[path] = client;
            }

            var allPaths = inboxPaths.Concat(clientPathOwners.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var messages = await _catalogService.ReadAsync(allPaths, _settings.GetEffectiveLicenseIndexPath());
            var byPath = messages.ToDictionary(message => message.FullPath, StringComparer.OrdinalIgnoreCase);

            _allInboxRows.Clear();
            foreach (var path in inboxPaths)
            {
                var message = byPath.GetValueOrDefault(path) ?? new ParsedLicenseMessage { FullPath = path, FileName = Path.GetFileName(path), Error = "Mail non analysé" };
                var row = new LicenseInboxRowViewModel(_licenseService.Match(message, _clients));
                row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(LicenseInboxRowViewModel.IsSelected)) UpdateDispatchSummary(); };
                _allInboxRows.Add(row);
            }

            _allTrackingRows.Clear();
            foreach (var client in _clients.OrderBy(client => client.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var clientMessages = clientPathOwners.Where(pair => pair.Value.MetadataId == client.MetadataId)
                    .Select(pair => byPath.GetValueOrDefault(pair.Key))
                    .Where(message => message is not null)
                    .Cast<ParsedLicenseMessage>()
                    .ToArray();
                _allTrackingRows.Add(new ClientLicenseRowViewModel(_licenseService.Summarize(client, clientMessages, DateTime.Today, _settings.LicenseRenewalWarningDays)));
            }

            RefreshInbox();
            RefreshTracking();
            UpdateSummary();
        }
        catch (Exception exception)
        {
            IdeoDialog.Error("Licences indisponibles", $"Impossible d’analyser les licences.\n\n{exception.Message}", "Le facteur numérique a probablement perdu son GPS.");
            ScanSummaryText.Text = "Lecture impossible · vérifie les chemins dans les paramètres";
        }
        finally
        {
            IsEnabled = true;
            _loading = false;
        }
    }

    private void RefreshInbox()
    {
        InboxRows.Clear();
        foreach (var row in _allInboxRows) InboxRows.Add(row);
        InboxEmptyText.Visibility = InboxRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDispatchSummary();
    }

    private void RefreshTracking()
    {
        if (!IsInitialized) return;
        var search = TrackingSearchText.Text.Trim();
        var state = (TrackingStatusFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var filtered = _allTrackingRows.Where(row =>
            (string.IsNullOrWhiteSpace(search) || row.ClientName.Contains(search, StringComparison.CurrentCultureIgnoreCase) || row.Client.Emails.Any(email => email.Contains(search, StringComparison.OrdinalIgnoreCase))) &&
            (state == "All" ||
             state == "Attention" && (row.Summary.State == ClientLicenseState.Expired || row.Summary.State == ClientLicenseState.Missing || row.Summary.State == ClientLicenseState.Unreadable) ||
             row.Summary.State.ToString() == state));
        TrackingRows.Clear();
        foreach (var row in filtered) TrackingRows.Add(row);
        TrackingEmptyText.Visibility = TrackingRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSummary()
    {
        var matched = _allInboxRows.Count(row => row.CanDispatch);
        var unidentified = _allInboxRows.Count - matched;
        var renew = _allTrackingRows.Count(row => row.Summary.State == ClientLicenseState.RenewSoon);
        var attention = _allTrackingRows.Count(row => row.Summary.State is ClientLicenseState.Expired or ClientLicenseState.Missing or ClientLicenseState.Unreadable) + unidentified;
        InboxCountText.Text = _allInboxRows.Count.ToString("N0");
        MatchedCountText.Text = matched.ToString("N0");
        UnmatchedCountText.Text = unidentified.ToString("N0");
        RenewCountText.Text = renew.ToString("N0");
        AttentionCountText.Text = attention.ToString("N0");
        ScanSummaryText.Text = $"{_allInboxRows.Count:N0} mail(s) en attente · {_clients.Count:N0} client(s) suivis";
    }

    private void UpdateDispatchSummary()
    {
        var selected = _allInboxRows.Count(row => row.IsSelected);
        var ready = _allInboxRows.Count(row => row.IsSelected && row.CanDispatch);
        DispatchSummaryText.Text = selected == 0 ? "Aucun mail sélectionné." : $"{selected} sélectionné(s) · {ready} prêt(s) à distribuer";
        DispatchButton.IsEnabled = ready > 0;
        TrashSelectedButton.IsEnabled = selected > 0;
    }

    private void InboxTab_Click(object sender, RoutedEventArgs e) => ShowInbox();
    private void TrackingTab_Click(object sender, RoutedEventArgs e) => ShowTracking();
    private void ShowInbox()
    {
        InboxPanel.Visibility = Visibility.Visible;
        TrackingPanel.Visibility = Visibility.Collapsed;
        InboxTabButton.Style = (Style)FindResource("PrimaryButton");
        TrackingTabButton.Style = (Style)FindResource(typeof(Button));
        CloseLicenseDetails();
    }
    private void ShowTracking()
    {
        InboxPanel.Visibility = Visibility.Collapsed;
        TrackingPanel.Visibility = Visibility.Visible;
        TrackingTabButton.Style = (Style)FindResource("PrimaryButton");
        InboxTabButton.Style = (Style)FindResource(typeof(Button));
        CloseLicenseDetails();
    }

    private void InboxList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InboxList.SelectedItem is not LicenseInboxRowViewModel row) return;
        _selectedInboxRow = row;
        _selectedTrackingRow = null;
        LicenseDetailEyebrow.Text = "MAIL DE LICENCE";
        LicenseDetailTitle.Text = row.FileName;
        LicenseDetailState.Text = row.StatusShort;
        LicenseDetailState.Foreground = BrushFrom(row.StatusColor);
        LicenseDetailStateHint.Text = row.MatchDisplay;
        LicenseDetailExpiry.Text = row.ExpiryDisplay;
        LicenseDetailClient.Text = row.ClientDisplay;
        LicenseDetailRecipients.Text = row.Recipients;
        LicenseDetailDescription.Text = row.Subject;
        LicenseDetailAssignButton.Visibility = Visibility.Visible;
        LicenseDetailTrashButton.Visibility = Visibility.Visible;
        LicenseDetailPlannerButton.Visibility = Visibility.Collapsed;
        LicenseDetailsPanel.Visibility = Visibility.Visible;
    }

    private void TrackingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TrackingList.SelectedItem is not ClientLicenseRowViewModel row) return;
        _selectedTrackingRow = row;
        _selectedInboxRow = null;
        LicenseDetailEyebrow.Text = "SUIVI CLIENT";
        LicenseDetailTitle.Text = row.ClientName;
        LicenseDetailState.Text = row.StateDisplay;
        LicenseDetailState.Foreground = BrushFrom(row.StateColor);
        LicenseDetailStateHint.Text = row.DaysDisplay;
        LicenseDetailExpiry.Text = row.ExpiryDisplay;
        LicenseDetailClient.Text = $"{row.ClientName} · {row.Variant}";
        LicenseDetailRecipients.Text = row.Client.Emails.Count == 0 ? "Aucune adresse renseignée" : string.Join("\n", row.Client.Emails);
        LicenseDetailDescription.Text = $"{row.ValidLicenseDisplay} · {row.MessageDisplay}. La date la plus lointaine est utilisée lorsqu’un client possède plusieurs licences.";
        LicenseDetailAssignButton.Visibility = Visibility.Collapsed;
        LicenseDetailTrashButton.Visibility = Visibility.Collapsed;
        LicenseDetailPlannerButton.Visibility = row.Summary.State is ClientLicenseState.RenewSoon or ClientLicenseState.Expired ? Visibility.Visible : Visibility.Collapsed;
        LicenseDetailsPanel.Visibility = Visibility.Visible;
    }

    private void CloseLicenseDetails_Click(object sender, RoutedEventArgs e) => CloseLicenseDetails();
    private void CloseLicenseDetails()
    {
        LicenseDetailsPanel.Visibility = Visibility.Collapsed;
        InboxList.SelectedItem = null;
        TrackingList.SelectedItem = null;
        _selectedInboxRow = null;
        _selectedTrackingRow = null;
    }

    private void DetailOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInboxRow is not null) OpenMessage_Click(new Button { Tag = _selectedInboxRow }, e);
        else if (_selectedTrackingRow is not null) OpenClientLicenseFolder_Click(new Button { Tag = _selectedTrackingRow }, e);
    }

    private void DetailAssign_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInboxRow is not null) Assign_Click(new Button { Tag = _selectedInboxRow }, e);
    }

    private void DetailTrash_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInboxRow is not null) TrashMessage_Click(new Button { Tag = _selectedInboxRow }, e);
    }

    private void SummaryFilter_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag?.ToString();
        if (tag is "Inbox" or "Matched" or "Unmatched")
        {
            ShowInbox();
            foreach (var row in _allInboxRows) row.IsSelected = tag switch { "Matched" => row.CanDispatch, "Unmatched" => !row.CanDispatch, _ => true };
            return;
        }
        ShowTracking();
        TrackingStatusFilter.SelectedValue = null;
        var target = tag;
        foreach (ComboBoxItem item in TrackingStatusFilter.Items)
        {
            if (item.Tag?.ToString() == target) { TrackingStatusFilter.SelectedItem = item; break; }
        }
    }

    private void SelectAllInbox_Click(object sender, RoutedEventArgs e) { foreach (var row in _allInboxRows) row.IsSelected = true; }
    private void UnselectAllInbox_Click(object sender, RoutedEventArgs e) { foreach (var row in _allInboxRows) row.IsSelected = false; }
    private void TrackingFilterChanged(object sender, RoutedEventArgs e) => RefreshTracking();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void TrackingListViewButton_Click(object sender, RoutedEventArgs e) => ApplyTrackingView(false);

    private void TrackingCardViewButton_Click(object sender, RoutedEventArgs e) => ApplyTrackingView(true);

    private void ApplyTrackingView(bool useCards, bool persist = true)
    {
        TrackingList.ItemTemplate = (DataTemplate)FindResource(useCards ? "LicenseTrackingCardTemplate" : "LicenseTrackingListTemplate");
        TrackingList.ItemContainerStyle = (Style)FindResource(useCards ? "LicenseTrackingCardItemStyle" : "LicenseTrackingListItemStyle");
        TrackingList.ItemsPanel = (ItemsPanelTemplate)FindResource(useCards ? "LicenseTrackingCardItemsPanel" : "LicenseTrackingListItemsPanel");
        ScrollViewer.SetCanContentScroll(TrackingList, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(TrackingList, ScrollBarVisibility.Disabled);

        var active = (Brush)FindResource("AccentDarkBrush");
        var inactive = (Brush)FindResource("SurfaceAltBrush");
        TrackingListViewButton.Background = useCards ? inactive : active;
        TrackingCardViewButton.Background = useCards ? active : inactive;
        TrackingListViewButton.Foreground = useCards ? (Brush)FindResource("TextBrush") : Brushes.White;
        TrackingCardViewButton.Foreground = useCards ? Brushes.White : (Brush)FindResource("TextBrush");

        if (!persist) return;
        _uiPreferences.LicenseTrackingUseCardView = useCards;
        _uiPreferencesService.Save(_uiPreferences);
    }

    private void OpenPlanner_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_settings.LicensePlannerUrl) { UseShellExecute = true }); }
        catch (Exception exception) { IdeoDialog.Warning("Ouverture impossible", exception.Message); }
    }

    private void OpenTrash_Click(object sender, RoutedEventArgs e)
    {
        var path = _settings.GetEffectiveLicenseTrashPath();
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception exception) { IdeoDialog.Warning("Corbeille inaccessible", exception.Message); }
    }

    private async void TrashMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LicenseInboxRowViewModel row })
            await TrashRowsAsync([row]);
    }

    private async void TrashSelected_Click(object sender, RoutedEventArgs e) =>
        await TrashRowsAsync(_allInboxRows.Where(row => row.IsSelected).ToArray());

    private async Task TrashRowsAsync(IReadOnlyList<LicenseInboxRowViewModel> rows)
    {
        if (rows.Count == 0) return;
        if (!IdeoDialog.Confirm(
                "Mettre les mails en corbeille",
                $"{rows.Count} mail(s) seront retirés de la boîte d’arrivée et déplacés dans la corbeille récupérable.",
                "Ils ne disparaissent pas : ils prennent simplement des vacances dans le dossier d’à côté.",
                "Mettre en corbeille")) return;

        IsEnabled = false;
        var successes = 0;
        var errors = new List<string>();
        try
        {
            var trashPath = _settings.GetEffectiveLicenseTrashPath();
            foreach (var row in rows)
            {
                var result = await Task.Run(() => _licenseService.MoveVerified(row.Message.FullPath, trashPath));
                if (result.Success) successes++; else errors.Add($"{row.FileName} : {result.Error}");
                await _catalogService.AppendHistoryAsync(_settings.GetEffectiveLicenseHistoryPath(), new LicenseHistoryEntry
                {
                    Action = "trash",
                    SourceFile = row.Message.FullPath,
                    ExpiryDate = row.Message.ExpiryDate,
                    DestinationFile = result.DestinationPath,
                    Success = result.Success,
                    Details = result.Error
                });
            }
        }
        finally { IsEnabled = true; }

        if (errors.Count == 0)
            IdeoDialog.Info("Boîte allégée", $"{successes} mail(s) placé(s) dans la corbeille récupérable.", "Aucun .msg n’a été maltraité pendant l’opération.");
        else
            IdeoDialog.Warning("Mise en corbeille partielle", $"{successes} mail(s) déplacé(s), {errors.Count} erreur(s).\n\n{string.Join("\n", errors.Take(8))}");
        await LoadAsync();
    }

    private void OpenMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LicenseInboxRowViewModel row }) return;
        try { Process.Start(new ProcessStartInfo(row.Message.FullPath) { UseShellExecute = true }); }
        catch (Exception exception) { IdeoDialog.Warning("Mail impossible à ouvrir", exception.Message); }
    }

    private async void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LicenseInboxRowViewModel row }) return;
        var dialog = new LicenseAssignmentWindow(row, _clients) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.SelectedClient is not { } client) return;
        if (dialog.SaveAddress && dialog.SelectedAddress is { } address)
        {
            var normalized = LicenseManagementService.NormalizeEmail(address);
            var conflict = _registry.Clients.FirstOrDefault(metadata => metadata.Id != client.MetadataId && metadata.Emails.Any(email => LicenseManagementService.NormalizeEmail(email) == normalized));
            if (conflict is not null)
            {
                IdeoDialog.Warning("Adresse déjà utilisée", $"{address} est déjà associée à un autre client. L’adresse n’a pas été ajoutée afin d’éviter un futur tri au hasard.");
            }
            else
            {
                var metadata = FindMetadata(client);
                if (!metadata.Emails.Any(email => LicenseManagementService.NormalizeEmail(email) == normalized))
                {
                    metadata.Emails.Add(address.Trim());
                    metadata.UpdatedAtUtc = DateTime.UtcNow;
                    await _registryService.SaveAsync(_clientSettings.ClientRegistryPath, _registry);
                    await _registryService.AppendHistoryAsync(_clientSettings.ClientHistoryPath, new ClientHistoryEntry { Action = "license_email_added", ClientName = client.DisplayName, After = address.Trim(), Details = row.FileName });
                }
            }
        }
        row.Assign(client, dialog.SelectedAddress);
        UpdateSummary();
        UpdateDispatchSummary();
    }

    private async void Dispatch_Click(object sender, RoutedEventArgs e)
    {
        var rows = _allInboxRows.Where(row => row.IsSelected && row.CanDispatch && row.AssignedClient is not null).ToArray();
        if (rows.Length == 0) { IdeoDialog.Info("Rien à distribuer", "Aucun mail sélectionné ne possède encore de client identifié."); return; }
        var inactive = rows.Count(row => row.AssignedClient!.IsSubscribed == false);
        var detail = inactive > 0 ? $"\n\nAttention : {inactive} licence(s) visent un client hors mise à jour." : string.Empty;
        if (!IdeoDialog.Confirm(
                "Distribuer les licences",
                $"{rows.Length} mail(s) seront copiés, contrôlés puis retirés de la boîte d’arrivée.{detail}",
                "Chaque enveloppe a trouvé sa boîte aux lettres. Vérifions tout de même le numéro.",
                "Distribuer")) return;

        IsEnabled = false;
        var successes = 0;
        var errors = new List<string>();
        try
        {
            foreach (var row in rows)
            {
                var client = row.AssignedClient!;
                var destinationFolder = Path.Combine(client.FullPath, _settings.LicenseClientFolderName);
                var result = await Task.Run(() => _licenseService.MoveVerified(row.Message.FullPath, destinationFolder));
                if (result.Success) successes++; else errors.Add($"{row.FileName} : {result.Error}");
                await _catalogService.AppendHistoryAsync(_settings.GetEffectiveLicenseHistoryPath(), new LicenseHistoryEntry
                {
                    Action = "dispatch",
                    SourceFile = row.Message.FullPath,
                    ClientName = client.DisplayName,
                    MatchedAddress = row.AssignedAddress,
                    ExpiryDate = row.Message.ExpiryDate,
                    DestinationFile = result.DestinationPath,
                    Success = result.Success,
                    Details = result.Error
                });
            }
        }
        finally { IsEnabled = true; }

        if (errors.Count == 0)
            IdeoDialog.Info("Distribution terminée", $"{successes} mail(s) rangé(s) et vérifié(s).", "La boîte d’arrivée respire à nouveau.");
        else
            IdeoDialog.Warning("Distribution partielle", $"{successes} mail(s) distribué(s), {errors.Count} erreur(s).\n\n{string.Join("\n", errors.Take(8))}", "Les mails en erreur sont restés dans la boîte d’arrivée.");
        await LoadAsync();
    }

    private void OpenClientLicenseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientLicenseRowViewModel row }) return;
        var path = Path.Combine(row.Client.FullPath, _settings.LicenseClientFolderName);
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception exception) { IdeoDialog.Warning("Ouverture impossible", exception.Message); }
    }

    private ClientMetadata FindMetadata(ClientRecord client)
    {
        var metadata = _registry.Clients.FirstOrDefault(item => item.Id == client.MetadataId)
                       ?? _registry.Clients.FirstOrDefault(item => !item.IsArchived && item.FolderName.Equals(client.FolderName, StringComparison.OrdinalIgnoreCase));
        if (metadata is not null) return metadata;
        metadata = new ClientMetadata { Id = client.MetadataId, CanonicalName = ClientFolderNamingService.BuildCanonicalName(client.BaseName), FolderName = client.FolderName, Emails = client.Emails.ToList() };
        _registry.Clients.Add(metadata);
        return metadata;
    }

    private static System.Windows.Media.SolidColorBrush BrushFrom(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
