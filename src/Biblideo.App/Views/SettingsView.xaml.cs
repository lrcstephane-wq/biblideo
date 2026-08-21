using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly Action<AppSettings> _saved;

    public SettingsView(AppSettings settings, SettingsService settingsService, Action<AppSettings> saved)
    {
        _settings = settings;
        _settingsService = settingsService;
        _saved = saved;
        InitializeComponent();
        LoadValues();
    }

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
        SevenZipText.Text = _settings.SevenZipExecutablePath;
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

    private void TestPathsButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPaths = new[] { SourceScText.Text, SourceEpText.Text, ClientsRootText.Text, IdeoRootText.Text, ProductStagingText.Text, ProductArchivesText.Text, ReportsText.Text };
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
        _settings.SourceScPath = SourceScText.Text.Trim();
        _settings.SourceEpPath = SourceEpText.Text.Trim();
        _settings.ClientsRootPath = ClientsRootText.Text.Trim();
        _settings.IdeoSolutionsRootPath = IdeoRootText.Text.Trim();
        _settings.ProductStagingPath = ProductStagingText.Text.Trim();
        _settings.ProductArchivesPath = ProductArchivesText.Text.Trim();
        _settings.ReportsPath = ReportsText.Text.Trim();
        _settings.MonthlyPackagesRootPath = IdeoRootText.Text.Trim();
        _settings.SevenZipExecutablePath = SevenZipText.Text.Trim();
        try
        {
            await _settingsService.SaveAsync(_settings, SharedSettingsText.Text.Trim());
            _saved(_settings);
            TestResultText.Text = "Configuration partagée enregistrée · copie locale actualisée.";
            TestResultText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            TestResultText.Text = "Enregistrement impossible sur le réseau.";
            TestResultText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            MessageBox.Show(
                $"La configuration partagée n’a pas été modifiée.\n\n{exception.Message}",
                "Enregistrement impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
