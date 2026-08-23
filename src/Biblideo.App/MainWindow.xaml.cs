using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Biblideo.App.Views;
using Biblideo.App.Services;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly ClientModuleSettingsService _clientSettingsService = new();
    private readonly ApplicationUpdateService _updateService = new();
    private AppSettings _settings = new();
    private ClientModuleSettings _clientSettings = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();
        _clientSettings = await _clientSettingsService.LoadAsync(_settings);
        if (_settingsService.LastLoadUsedCache)
        {
            AppModeText.Text = "Configuration réseau indisponible · copie locale utilisée";
            AppModeText.Foreground = System.Windows.Media.Brushes.Khaki;
        }
        ShowProductPackages();
        VersionText.Text = $"v{_updateService.CurrentVersion}";
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var version = await _updateService.CheckAsync();
            if (version is null)
            {
                UpdateButton.Content = "À jour";
                UpdateButton.ToolTip = $"Biblidéo {_updateService.CurrentVersion} · cliquer pour vérifier";
                return;
            }
            UpdateButton.Content = $"Installer v{version}";
            UpdateButton.Style = (Style)FindResource("PrimaryButton");
        }
        catch
        {
            // Une indisponibilité d’Internet ne doit jamais bloquer l’application métier.
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateService.AvailableVersion is null)
        {
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "Recherche…";
            try
            {
                var version = await _updateService.CheckAsync();
                if (version is null)
                {
                    UpdateButton.Content = "À jour";
                    IdeoDialog.Info("Mise à jour", $"Biblidéo {_updateService.CurrentVersion} est déjà à jour.", "Pour une fois qu’un logiciel ne réclame rien, profitons-en.");
                    return;
                }
                UpdateButton.Content = $"Installer v{version}";
                UpdateButton.Style = (Style)FindResource("PrimaryButton");
            }
            catch (Exception exception)
            {
                UpdateButton.Content = "Réessayer";
                IdeoDialog.Warning("Vérification impossible", exception.Message);
                return;
            }
            finally
            {
                UpdateButton.IsEnabled = true;
            }
        }

        if (!IdeoDialog.Confirm(
                "Mise à jour Biblidéo",
                $"La version {_updateService.AvailableVersion} va être téléchargée. Biblidéo se fermera, remplacera son exécutable puis redémarrera automatiquement.",
                "Une courte pause café numérique, puis tout repart.",
                "Mettre à jour"))
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
            IdeoDialog.Warning("Mise à jour impossible", exception.Message);
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination }) return;
        foreach (var button in NavigationPanel.Children.OfType<Button>())
            button.Style = (Style)FindResource(button == sender ? "SideNavActiveButton" : "SideNavButton");
        if (sender is Button selectedButton)
            selectedButton.Style = (Style)FindResource("SideNavActiveButton");
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
                PageContent.Content = new SettingsView(_settings, _clientSettings, _settingsService, _clientSettingsService, OnSettingsSaved);
                break;
            case "Clients":
                PageTitle.Text = "Clients";
                PageContent.Content = new ClientsView(ClientModuleContext.Create(_settings, _clientSettings));
                break;
            case "Licenses":
                PageTitle.Text = "Licences";
                PageContent.Content = new LicensesView(_settings, ClientModuleContext.Create(_settings, _clientSettings));
                break;
            default:
                PageTitle.Text = destination switch
                {
                    "Monthly" => "Packs mensuels",
                    "Deployment" => "Déploiement",
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

    private void OnSettingsSaved(AppSettings settings, ClientModuleSettings clientSettings)
    {
        _settings = settings;
        _clientSettings = clientSettings;
    }

    private void ApplyDarkTitleBar()
    {
        if (!OperatingSystem.IsWindows()) return;
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
