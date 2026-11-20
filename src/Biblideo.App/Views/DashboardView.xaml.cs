using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Biblideo.App.Services;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public partial class DashboardView : UserControl
{
    private readonly AppSettings _settings;
    private readonly ClientModuleContext _clientSettings;
    private readonly Action<string> _navigate;
    private readonly ClientRegistryService _registryService = new();
    private readonly ClientManagementService _clientService = new();
    private readonly LicenseManagementService _licenseService = new();
    private readonly LicenseCatalogService _catalogService = new();
    private bool _loading;

    public DashboardView(AppSettings settings, ClientModuleContext clientSettings, Action<string> navigate)
    {
        _settings = settings;
        _clientSettings = clientSettings;
        _navigate = navigate;
        InitializeComponent();
        SetDateAndPhrase();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var priorities = new List<DashboardPriority>();
            var productCount = CountDirectories(_settings.ProductStagingPath);
            var inboxPath = _settings.GetEffectiveLicenseInboxPath();
            var inboxPaths = Directory.Exists(inboxPath)
                ? Directory.EnumerateFiles(inboxPath, "*.msg", SearchOption.TopDirectoryOnly).ToArray()
                : [];

            ClientScanResult scan = new();
            try
            {
                var registry = await _registryService.LoadAsync(_clientSettings.ClientRegistryPath);
                scan = await Task.Run(() => _clientService.Scan(_clientSettings, registry));
            }
            catch (Exception exception)
            {
                priorities.Add(new DashboardPriority("Racine clients inaccessible", exception.Message, "#EF7777", "Settings", "Vérifier"));
            }

            var renewCount = 0;
            var licenseAttention = 0;
            if (scan.Clients.Count > 0)
            {
                try
                {
                    var pathOwners = new Dictionary<string, ClientRecord>(StringComparer.OrdinalIgnoreCase);
                    foreach (var client in scan.Clients)
                    {
                        var folder = Path.Combine(client.FullPath, _settings.LicenseClientFolderName);
                        if (!Directory.Exists(folder)) continue;
                        foreach (var path in Directory.EnumerateFiles(folder, "*.msg", SearchOption.TopDirectoryOnly)) pathOwners[path] = client;
                    }
                    var messages = await _catalogService.ReadAsync(pathOwners.Keys.ToArray(), _settings.GetEffectiveLicenseIndexPath(), updateIndex: false);
                    var byPath = messages.ToDictionary(message => message.FullPath, StringComparer.OrdinalIgnoreCase);
                    foreach (var client in scan.Clients)
                    {
                        var clientMessages = pathOwners
                            .Where(pair => pair.Value.MetadataId == client.MetadataId)
                            .Select(pair => byPath.GetValueOrDefault(pair.Key))
                            .Where(message => message is not null)
                            .Cast<ParsedLicenseMessage>()
                            .ToArray();
                        var summary = _licenseService.Summarize(client, clientMessages, DateTime.Today, _settings.LicenseRenewalWarningDays);
                        if (summary.State == ClientLicenseState.RenewSoon) renewCount++;
                        if (summary.State is ClientLicenseState.Expired or ClientLicenseState.Missing or ClientLicenseState.Unreadable) licenseAttention++;
                    }
                }
                catch (Exception exception)
                {
                    priorities.Add(new DashboardPriority("Suivi des licences incomplet", exception.Message, "#D89B54", "Licenses", "Consulter"));
                }
            }

            var structureWarnings = scan.Clients.Count(client => !client.StructureIsValid);
            var emailWarnings = scan.Clients.Count(client => client.Emails.Count == 0);
            if (inboxPaths.Length > 0) priorities.Add(new DashboardPriority($"{inboxPaths.Length} licence(s) à distribuer", "La boîte d’arrivée attend son facteur numérique.", "#C68BE3", "Licenses", "Trier"));
            if (renewCount > 0) priorities.Add(new DashboardPriority($"{renewCount} licence(s) bientôt à renouveler", $"Échéance dans les {_settings.LicenseRenewalWarningDays} prochains jours.", "#D89B54", "Licenses", "Voir"));
            if (licenseAttention > 0) priorities.Add(new DashboardPriority($"{licenseAttention} licence(s) à vérifier", "Licence absente, expirée ou message illisible.", "#EF7777", "Licenses", "Contrôler"));
            if (structureWarnings > 0) priorities.Add(new DashboardPriority($"{structureWarnings} structure(s) client à réparer", "Des sous-dossiers obligatoires sont manquants.", "#EF7777", "Clients", "Réparer"));
            if (emailWarnings > 0) priorities.Add(new DashboardPriority($"{emailWarnings} client(s) sans adresse", "Les futures licences auront besoin d’un destinataire connu.", "#7FB4EF", "Clients", "Compléter"));
            if (productCount > 0) priorities.Add(new DashboardPriority($"{productCount} package(s) produit en attente", "Prêts à rejoindre un prochain pack mensuel.", "#4D8BD4", "Product", "Ouvrir"));

            ProductCountText.Text = productCount.ToString("N0");
            SubscribedCountText.Text = scan.Clients.Count(client => client.IsSubscribed).ToString("N0");
            ClientCountCaption.Text = $"sur {scan.Clients.Count:N0} clients";
            InboxCountText.Text = inboxPaths.Length.ToString("N0");
            RenewCountText.Text = (renewCount + licenseAttention).ToString("N0");
            PriorityList.ItemsSource = priorities.Take(6);
            PriorityCountText.Text = priorities.Count == 0 ? "Tout roule" : $"{priorities.Count} sujet(s)";
            PriorityEmptyText.Visibility = priorities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CockpitSummaryText.Text = priorities.Count == 0
                ? "Les accès répondent et aucune urgence n’est détectée. Voilà une journée qui commence sans effet spécial."
                : $"{priorities.Count} point(s) méritent un regard. Les raccourcis ci-dessous t’emmènent directement au bon endroit.";

            var accessStates = new[]
            {
                SetAccess(ClientsAccessText, _settings.ClientsRootPath),
                SetAccess(PackagesAccessText, _settings.ProductStagingPath),
                SetAccess(LicensesAccessText, inboxPath),
                SetFileAccess(ConfigAccessText, _settings.SharedSettingsPath)
            };
            var allAccess = accessStates.All(value => value);
            SystemStatusText.Text = allAccess ? "Tous les accès répondent" : "Un accès demande ton attention";
            SystemStatusText.Foreground = BrushFrom(allAccess ? "#56C99B" : "#D89B54");
            SystemDot.Fill = BrushFrom(allAccess ? "#56C99B" : "#D89B54");

            var activities = ReadRecentActivity(_clientSettings.ClientHistoryPath, _settings.GetEffectiveLicenseHistoryPath());
            ActivityList.ItemsSource = activities;
            ActivityEmptyText.Visibility = activities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            CockpitSummaryText.Text = $"Le cockpit n’a pas pu terminer son tour d’horizon : {exception.Message}";
            SystemStatusText.Text = "Contrôle incomplet";
            SystemDot.Fill = BrushFrom("#EF7777");
        }
        finally { _loading = false; }
    }

    private void SetDateAndPhrase()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var raw = DateTime.Today.ToString("dddd d MMMM yyyy", culture);
        TodayText.Text = char.ToUpper(raw[0], culture) + raw[1..];
        string[] phrases =
        [
            "Un bon package est un package contrôlé. Le café, lui, reste en option.",
            "Aujourd’hui, on automatise les corvées et on garde les bonnes idées.",
            "Les dossiers bien rangés dorment mieux. Nous aussi.",
            "SC ou EP : choisir, c’est déjà éviter une catastrophe créative.",
            "Petit contrôle maintenant, grande tranquillité chez le client.",
            "Le progrès, c’est surtout moins de clics inutiles avant midi.",
            "Une licence bien distribuée est une licence qui ne téléphone pas demain.",
            "Biblidéo surveille les détails pendant que les humains gardent les bonnes idées."
        ];
        DailyPhraseText.Text = $"« {phrases[DateOnly.FromDateTime(DateTime.Today).DayNumber % phrases.Length]} »";
    }

    private static int CountDirectories(string path) => Directory.Exists(path)
        ? Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).Count()
        : 0;

    private static bool SetAccess(TextBlock target, string path)
    {
        var available = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        target.Text = available ? "Accessible" : "Indisponible";
        target.Foreground = BrushFrom(available ? "#56C99B" : "#EF7777");
        return available;
    }

    private static bool SetFileAccess(TextBlock target, string path)
    {
        var available = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        target.Text = available ? "Accessible" : "Indisponible";
        target.Foreground = BrushFrom(available ? "#56C99B" : "#EF7777");
        return available;
    }

    private static List<DashboardActivity> ReadRecentActivity(params string[] paths)
    {
        var result = new List<(DateTime Timestamp, DashboardActivity Item)>();
        foreach (var path in paths.Where(File.Exists))
        {
            foreach (var line in OperationHistory.ReadEntries(path).TakeLast(80))
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var timestamp = ReadDate(root, "TimestampUtc");
                    var action = ReadString(root, "Action") ?? "operation";
                    var subject = ReadString(root, "ClientName") ?? Path.GetFileName(ReadString(root, "SourceFile") ?? "Élément");
                    var detail = ReadString(root, "Details") ?? FriendlyAction(action);
                    result.Add((timestamp, new DashboardActivity(timestamp.ToLocalTime().ToString("dd/MM HH:mm"), $"{FriendlyAction(action)} · {subject}", detail)));
                }
                catch { }
            }
        }
        return result.OrderByDescending(item => item.Timestamp).Take(6).Select(item => item.Item).ToList();
    }

    private static DateTime ReadDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDateTime(out var parsed) ? parsed : DateTime.MinValue;
    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string FriendlyAction(string action) => action switch
    {
        "create" => "Client créé",
        "edit" => "Client modifié",
        "subscription_change" => "Abonnement modifié",
        "repair_structure" => "Structure réparée",
        "archive" => "Client archivé",
        "dispatch" => "Licence distribuée",
        "trash" => "Mail mis en corbeille",
        "license_email_added" => "Adresse licence mémorisée",
        _ => "Opération enregistrée"
    };
    private static SolidColorBrush BrushFrom(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string destination }) _navigate(destination);
    }

    private sealed record DashboardPriority(string Title, string Detail, string Accent, string Destination, string ActionLabel);
    private sealed record DashboardActivity(string When, string Title, string Detail);
}
