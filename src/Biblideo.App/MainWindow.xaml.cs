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
    private readonly SettingsService _settingsService = new(NexusPreviewProfile.Enabled ? NexusPreviewProfile.DataRoot : null);
    private readonly ClientModuleSettingsService _clientSettingsService = new(NexusPreviewProfile.Enabled ? NexusPreviewProfile.DataRoot : null);
    private readonly ApplicationUpdateService _updateService = new();
    private bool _navigationCollapsed;
    private AppSettings _settings = new();
    private ClientModuleSettings _clientSettings = new();

    public MainWindow()
    {
        InitializeComponent();
        if (NexusPreviewProfile.Enabled)
        {
            Title = "Idéo Nexus · Aperçu";
            UpdateButton.Visibility = Visibility.Collapsed;
            AppModeText.Text = "Aperçu · Données fictives";
            AppModeText.Visibility = Visibility.Visible;
        }
        foreach (var button in NavigationButtons) button.IsEnabled = false;
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        if (NexusPreviewProfile.Enabled)
        {
            (_settings, _clientSettings) = await NexusPreviewProfile.LoadAsync(NexusPreviewProfile.DataRoot);
        }
        else
        {
            _settings = await _settingsService.LoadAsync();
            _clientSettings = await _clientSettingsService.LoadAsync(_settings);
        }
        if (_settingsService.LastLoadUsedCache)
        {
            AppModeText.Text = "Configuration réseau indisponible · copie locale utilisée";
            AppModeText.Visibility = Visibility.Visible;
        }
        foreach (var button in NavigationButtons) button.IsEnabled = true;
        NavigateTo("Home");
        VersionText.Text = NexusPreviewProfile.Enabled ? "Aperçu" : $"v{_updateService.CurrentVersion}";
        if (!NexusPreviewProfile.Enabled) await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateButton.IsEnabled = false;
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
            UpdateButton.Content = "Vérifier la mise à jour";
            UpdateButton.ToolTip = "La dernière version publiée n’a pas pu être vérifiée.";
        }
        finally
        {
            UpdateButton.IsEnabled = true;
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
        NavigateTo(destination);
    }

    private IEnumerable<Button> NavigationButtons => NavigationPanel.Children.OfType<Button>().Append(SettingsButton);

    private void ToggleNavigation_Click(object sender, RoutedEventArgs e)
    {
        _navigationCollapsed = !_navigationCollapsed;
        NavigationColumn.Width = new GridLength(_navigationCollapsed ? 66 : 220);
        BrandPanel.Visibility = _navigationCollapsed ? Visibility.Collapsed : Visibility.Visible;
        WorkshopHeading.Visibility = BrandPanel.Visibility;
        foreach (var button in NavigationButtons)
            button.HorizontalContentAlignment = _navigationCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    }

    private void NavigateTo(string destination)
    {
        var clientContext = ClientModuleContext.Create(_settings, _clientSettings);
        (string Title, UserControl View) page = destination switch
        {
            "Home" => ("Accueil", new DashboardView(_settings, clientContext, NavigateTo)),
            "Clients" => ("Clients", new ClientsView(_settings, clientContext)),
            "AtlasLicenses" => ("Licences Atlas", new AtlasLicensesView(clientContext)),
            "Applications" => ("Applications", new ApplicationsView(NavigateTo)),
            "Licenses" => ("Licences Biblidéo", new LicensesView(_settings, clientContext)),
            "Product" => ("Packages produit", new ProductPackageView(_settings)),
            "Monthly" => ("Packs mensuels", new PlaceholderView("Packs mensuels")),
            "Deployment" => ("Déploiement", new PlaceholderView("Déploiement")),
            "Settings" => ("Paramètres", new SettingsView(_settings, _clientSettings, _settingsService, _clientSettingsService, OnSettingsSaved)),
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, null)
        };

        foreach (var button in NavigationButtons)
        {
            var style = Equals(button.Tag, destination) ? "NexusNavActiveButton" : "NexusNavButton";
            button.Style = (Style)FindResource(style);
        }

        PageTitle.Text = page.Title;
        PageContent.Content = page.View;
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
