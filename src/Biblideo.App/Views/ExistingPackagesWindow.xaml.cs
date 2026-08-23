using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Services;
using Biblideo.Core.Models;

namespace Biblideo.App.Views;

public enum ExistingPackageAction { None, Modify, Trash }

public partial class ExistingPackagesWindow : Window
{
    private readonly IReadOnlyList<ExistingProductPackage> _allPackages;
    private readonly ObservableCollection<ExistingProductPackage> _visiblePackages = [];

    public ExistingProductPackage? SelectedPackage { get; private set; }
    public ExistingPackageAction SelectedAction { get; private set; }

    public ExistingPackagesWindow(ExistingPackageCatalog catalog)
    {
        InitializeComponent();
        _allPackages = catalog.Packages;
        PackagesList.ItemsSource = _visiblePackages;
        WarningsText.Text = catalog.Warnings.Count == 0
            ? "Tous les dossiers reconnus respectent le nommage Biblidéo."
            : $"{catalog.Warnings.Count:N0} dossier(s) ignoré(s) · détail dans le prochain rapport.";
        ApplyFilter();
    }

    private void SearchText_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (!IsInitialized) return;
        var query = SearchText.Text.Trim();
        _visiblePackages.Clear();
        foreach (var package in _allPackages.Where(package =>
                     query.Length == 0 ||
                     package.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     package.Variant.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)))
            _visiblePackages.Add(package);
        SummaryText.Text = $"{_visiblePackages.Count:N0} package(s) affiché(s) · double-clic pour modifier";
        EmptyText.Visibility = _visiblePackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_visiblePackages.Count > 0 && PackagesList.SelectedItem is null) PackagesList.SelectedIndex = 0;
    }

    private void PackagesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Choose(ExistingPackageAction.Modify);
    private void ModifyButton_Click(object sender, RoutedEventArgs e) => Choose(ExistingPackageAction.Modify);
    private void TrashButton_Click(object sender, RoutedEventArgs e) => Choose(ExistingPackageAction.Trash);

    private void Choose(ExistingPackageAction action)
    {
        if (PackagesList.SelectedItem is not ExistingProductPackage package)
        {
            IdeoDialog.Info("Aucun package sélectionné", "Choisis d’abord un package dans la liste.", "Même la corbeille demande une adresse précise.");
            return;
        }
        SelectedPackage = package;
        SelectedAction = action;
        DialogResult = true;
        Close();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesList.SelectedItem is not ExistingProductPackage package) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{package.FullPath}\"") { UseShellExecute = true }); }
        catch (Exception exception) { IdeoDialog.Warning("Ouverture impossible", exception.Message); }
    }
}
