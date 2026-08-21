using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Views;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();
        ShowProductPackages();
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
