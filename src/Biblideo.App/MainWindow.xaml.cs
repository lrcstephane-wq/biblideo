using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Views;
using Biblideo.App.Services;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly ApplicationUpdateService _updateService = new();
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();
        if (_settingsService.LastLoadUsedCache)
        {
            AppModeText.Text = "Configuration réseau indisponible · copie locale utilisée";
            AppModeText.Foreground = System.Windows.Media.Brushes.Khaki;
        }
        ShowProductPackages();
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var version = await _updateService.CheckAsync();
            if (version is null) return;
            UpdateButton.Content = $"Mise à jour {version}";
            UpdateButton.Visibility = Visibility.Visible;
        }
        catch
        {
            // Une indisponibilité d’Internet ne doit jamais bloquer l’application métier.
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "La mise à jour va être téléchargée, puis Biblidéo redémarrera. Continuer ?",
                "Mise à jour Biblidéo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Téléchargement…";
        try
        {
            await _updateService.DownloadAndRestartAsync(progress =>
                Dispatcher.Invoke(() => UpdateButton.Content = $"Téléchargement {progress}%"));
        }
        catch (Exception exception)
        {
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = "Réessayer la mise à jour";
            MessageBox.Show(exception.Message, "Mise à jour impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination }) return;
        switch (destination)
        {
            case "Home":
                PageTitle.Text = "Vue d’ensemble";
                PageContent.Content = new DashboardView();
                break;
            case "Product":
                ShowProductPackages();
                break;
            case "Settings":
                PageTitle.Text = "Paramètres";
                PageContent.Content = new SettingsView(_settings, _settingsService, OnSettingsSaved);
                break;
            default:
                PageTitle.Text = destination switch
                {
                    "Monthly" => "Packs mensuels",
                    "Deployment" => "Déploiement",
                    "Clients" => "Clients",
                    "Licenses" => "Licences",
                    _ => destination
                };
                PageContent.Content = new PlaceholderView(PageTitle.Text);
                break;
        }
    }

    private void ShowProductPackages()
    {
        PageTitle.Text = "Packages produit";
        PageContent.Content = new ProductPackageView(_settings);
    }

    private void OnSettingsSaved(AppSettings settings)
    {
        _settings = settings;
    }
}
