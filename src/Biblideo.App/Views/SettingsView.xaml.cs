using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Biblideo.Core.Models;
using Biblideo.Core.Services;
using Biblideo.App.Services;

namespace Biblideo.App.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings;
    private readonly ClientModuleSettings _clientSettings;
    private readonly SettingsService _settingsService;
    private readonly ClientModuleSettingsService _clientSettingsService;
    private readonly Action<AppSettings, ClientModuleSettings> _saved;

    public SettingsView(
        AppSettings settings,
        ClientModuleSettings clientSettings,
        SettingsService settingsService,
        ClientModuleSettingsService clientSettingsService,
        Action<AppSettings, ClientModuleSettings> saved)
    {
        _settings = settings;
        _clientSettings = clientSettings;
        _settingsService = settingsService;
        _clientSettingsService = clientSettingsService;
        _saved = saved;
        InitializeComponent();
        LoadValues();
        if (NexusPreviewProfile.Enabled)
        {
            foreach (var input in Descendants(this).OfType<TextBox>()) input.IsReadOnly = true;
            foreach (var button in Descendants(this).OfType<Button>()) button.IsEnabled = false;
            TestResultText.Text = "Paramètres de démonstration · lecture seule";
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void OpenNexusRegistrySettings_Click(object sender, RoutedEventArgs e) =>
        new NexusConnectionWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void LoadValues()
    {
        SharedSettingsText.Text = _settings.SharedSettingsPath;
        SourceScText.Text = _settings.SourceScPath;
        SourceEpText.Text = _settings.SourceEpPath;
        ClientsRootText.Text = _settings.ClientsRootPath;
        IdeoRootText.Text = _settings.IdeoSolutionsRootPath;
        ProductStagingText.Text = _settings.ProductStagingPath;
        ProductArchivesText.Text = _settings.ProductArchivesPath;
        ReportsText.Text = _settings.ReportsPath;
        ProductTrashText.Text = _settings.ProductTrashPath;
        SevenZipText.Text = _settings.SevenZipExecutablePath;
        ClientArchiveText.Text = _clientSettings.ArchivePath;
        ClientRegistryText.Text = _clientSettings.RegistryPath;
        ClientHistoryText.Text = _clientSettings.HistoryPath;
        ClientMarkerText.Text = _clientSettings.SubscriptionMarker;
        ClientFoldersText.Text = string.Join("; ", _clientSettings.FolderStructure);
        ClientIgnoredFoldersText.Text = string.Join("; ", _clientSettings.IgnoredFolderNames);
        ClientRecipientsText.Text = string.Join("; ", _clientSettings.NotificationRecipients);
        LicenseInboxText.Text = _settings.GetEffectiveLicenseInboxPath();
        LicenseTrashText.Text = _settings.GetEffectiveLicenseTrashPath();
        LicenseIndexText.Text = _settings.GetEffectiveLicenseIndexPath();
        LicenseHistoryText.Text = _settings.GetEffectiveLicenseHistoryPath();
        LicenseWarningDaysText.Text = _settings.LicenseRenewalWarningDays.ToString();
        LicensePlannerText.Text = _settings.LicensePlannerUrl;
    }

    private void BrowseSharedSettings_Click(object sender, RoutedEventArgs e)
    {
        var currentDirectory = Path.GetDirectoryName(SharedSettingsText.Text);
        var dialog = new SaveFileDialog
        {
            Title = "Choisir le fichier de configuration partagée",
            Filter = "Configuration JSON|*.json",
            FileName = "biblideo.settings.json",
            InitialDirectory = currentDirectory is not null && Directory.Exists(currentDirectory)
                ? currentDirectory
                : SettingsDefaults.ApplicationDataRoot
        };
        if (dialog.ShowDialog() == true) SharedSettingsText.Text = dialog.FileName;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string textBoxName } || FindName(textBoxName) is not TextBox target) return;
        var dialog = new OpenFolderDialog { Title = "Choisir le dossier", InitialDirectory = Directory.Exists(target.Text) ? target.Text : string.Empty };
        if (dialog.ShowDialog() == true) target.Text = dialog.FolderName;
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choisir 7z.exe", Filter = "7-Zip|7z.exe|Exécutables|*.exe" };
        if (dialog.ShowDialog() == true) SevenZipText.Text = dialog.FileName;
    }

    private void BrowseDataFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var parts = tag.Split('|', 2);
        if (parts.Length != 2 || FindName(parts[0]) is not TextBox target) return;
        var currentDirectory = Path.GetDirectoryName(target.Text);
        var dialog = new SaveFileDialog
        {
            Title = "Choisir le fichier partagé",
            FileName = parts[1],
            Filter = parts[1].EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) ? "Historique JSONL|*.jsonl|Tous les fichiers|*.*" : "Configuration JSON|*.json|Tous les fichiers|*.*",
            InitialDirectory = currentDirectory is not null && Directory.Exists(currentDirectory) ? currentDirectory : SettingsDefaults.ApplicationDataRoot
        };
        if (dialog.ShowDialog() == true) target.Text = dialog.FileName;
    }

    private void TestPathsButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPaths = new[] { SourceScText.Text, SourceEpText.Text, ClientsRootText.Text, IdeoRootText.Text, ProductStagingText.Text, ProductArchivesText.Text, ReportsText.Text, ProductTrashText.Text, ClientArchiveText.Text, LicenseInboxText.Text, LicenseTrashText.Text };
        var validFolders = folderPaths.Count(Directory.Exists);
        var sevenZipValid = File.Exists(SevenZipText.Text);
        var sharedDirectory = Path.GetDirectoryName(SharedSettingsText.Text.Trim());
        var sharedAvailable = sharedDirectory is not null && Directory.Exists(sharedDirectory);
        TestResultText.Text = $"Configuration partagée {(sharedAvailable ? "accessible" : "inaccessible")} · {validFolders}/{folderPaths.Length} dossiers accessibles · 7-Zip {(sevenZipValid ? "détecté" : "introuvable")}";
        TestResultText.Foreground = sharedAvailable && validFolders == folderPaths.Length && sevenZipValid
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Khaki;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (NexusPreviewProfile.Enabled) return;
        var stagedSettings = new AppSettings
        {
            SharedSettingsPath = SharedSettingsText.Text.Trim(),
            SourceScPath = SourceScText.Text.Trim(),
            SourceEpPath = SourceEpText.Text.Trim(),
            ClientsRootPath = ClientsRootText.Text.Trim(),
            IdeoSolutionsRootPath = IdeoRootText.Text.Trim(),
            ProductStagingPath = ProductStagingText.Text.Trim(),
            ProductArchivesPath = ProductArchivesText.Text.Trim(),
            ReportsPath = ReportsText.Text.Trim(),
            ProductTrashPath = ProductTrashText.Text.Trim(),
            MonthlyPackagesRootPath = IdeoRootText.Text.Trim(),
            SevenZipExecutablePath = SevenZipText.Text.Trim(),
            LicenseInboxPath = LicenseInboxText.Text.Trim(),
            LicenseTrashPath = LicenseTrashText.Text.Trim(),
            LicenseIndexPath = LicenseIndexText.Text.Trim(),
            LicenseHistoryPath = LicenseHistoryText.Text.Trim(),
            LicenseClientFolderName = _settings.LicenseClientFolderName,
            LicenseRenewalWarningDays = int.TryParse(LicenseWarningDaysText.Text, out var warningDays) ? Math.Clamp(warningDays, 1, 90) : 10,
            LicensePlannerUrl = LicensePlannerText.Text.Trim()
        };
        var stagedClientSettings = new ClientModuleSettings
        {
            ArchivePath = ClientArchiveText.Text.Trim(),
            RegistryPath = ClientRegistryText.Text.Trim(),
            HistoryPath = ClientHistoryText.Text.Trim(),
            SubscriptionMarker = ClientMarkerText.Text.Trim(),
            BiblideoFolderName = _clientSettings.BiblideoFolderName,
            FolderStructure = ClientFoldersText.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IgnoredFolderNames = ClientIgnoredFoldersText.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            NotificationRecipients = ClientRecipientsText.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        if (stagedClientSettings.FolderStructure.Count == 0)
        {
            IdeoDialog.Warning("Structure client incomplète", "Au moins un sous-dossier client doit être configuré.");
            return;
        }
        if (!stagedClientSettings.FolderStructure.Contains(stagedClientSettings.BiblideoFolderName, StringComparer.OrdinalIgnoreCase))
        {
            IdeoDialog.Warning("Dossier Biblidéo manquant", $"La structure doit contenir {stagedClientSettings.BiblideoFolderName} pour détecter les déploiements.");
            return;
        }
        if (string.IsNullOrWhiteSpace(stagedClientSettings.SubscriptionMarker))
        {
            IdeoDialog.Warning("Marqueur obligatoire", "Le marqueur permettant d’identifier les clients sous mise à jour ne peut pas être vide.");
            return;
        }
        try
        {
            await _settingsService.SaveAsync(stagedSettings, stagedSettings.SharedSettingsPath);
            await _clientSettingsService.SaveAsync(stagedSettings, stagedClientSettings);
            _saved(stagedSettings, stagedClientSettings);
            TestResultText.Text = "Règles partagées enregistrées · chemins de ce poste actualisés.";
            TestResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            TestResultText.Text = "Enregistrement impossible sur le réseau.";
            TestResultText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            IdeoDialog.Error(
                "Enregistrement impossible",
                $"La configuration partagée n’a pas été modifiée.\n\n{exception.Message}",
                "La configuration a préféré rester là où elle était. Prudente, mais peu coopérative.");
        }
    }
}
