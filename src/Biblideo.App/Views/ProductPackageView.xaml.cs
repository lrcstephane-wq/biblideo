using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Biblideo.App.ViewModels;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public partial class ProductPackageView : UserControl
{
    private readonly AppSettings _settings;
    private readonly LibraryScanner _scanner = new();
    private readonly ProductPackageSimulationService _simulationService = new();
    private readonly HtmlReportService _reportService = new();
    private readonly ObservableCollection<ComponentRowViewModel> _rows = [];
    private ICollectionView? _rowsView;
    private LibraryScanResult? _scanResult;
    private LibraryFilterNode? _activeFilter;
    private int _nextCounter;

    public ProductPackageView(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Loaded += ProductPackageView_Loaded;
    }

    private void ProductPackageView_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshCounter();
        RefreshPackagePreview();
        RefreshSelectionSummary();
    }

    private LibraryVariant SelectedVariant =>
        VariantCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
        Enum.TryParse<LibraryVariant>(tag, out var variant)
            ? variant
            : LibraryVariant.SC;

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        var source = _settings.GetSourcePath(SelectedVariant);
        ScanProgress.Visibility = Visibility.Visible;
        StatusText.Text = $"Analyse en lecture seule : {source}";
        IsEnabled = false;
        try
        {
            _scanResult = await _scanner.ScanAsync(source);
            _rows.Clear();
            foreach (var component in _scanResult.Components)
            {
                var row = new ComponentRowViewModel(component);
                row.PropertyChanged += Row_PropertyChanged;
                _rows.Add(row);
            }

            _rowsView = CollectionViewSource.GetDefaultView(_rows);
            _rowsView.Filter = FilterRow;
            ComponentsGrid.ItemsSource = _rowsView;
            BuildLibraryTree();
            StatusText.Text = $"{_rows.Count:N0} composants analysés · {_scanResult.IgnoredBakCount:N0} .bak ignorés · {_scanResult.IgnoredCfgCount:N0} .cfg ignorés";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Analyse impossible", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Analyse interrompue.";
        }
        finally
        {
            IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
            RefreshSelectionSummary();
        }
    }

    private void BuildLibraryTree()
    {
        LibraryTree.Items.Clear();
        var all = new LibraryFilterNode { Label = $"Toutes ({_rows.Count:N0})" };
        LibraryTree.Items.Add(all);

        foreach (var libraryGroup in _rows.GroupBy(row => row.Library).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var library = new LibraryFilterNode
            {
                Label = $"{libraryGroup.Key} ({libraryGroup.Count():N0})",
                Library = libraryGroup.Key
            };
            foreach (var familyGroup in libraryGroup.GroupBy(row => row.Family).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                library.Children.Add(new LibraryFilterNode
                {
                    Label = $"{familyGroup.Key} ({familyGroup.Count():N0})",
                    Library = libraryGroup.Key,
                    Family = familyGroup.Key
                });
            }
            LibraryTree.Items.Add(library);
        }
    }

    private bool FilterRow(object item)
    {
        if (item is not ComponentRowViewModel row) return false;
        if (_activeFilter?.Library is { } library && !row.Library.Equals(library, StringComparison.OrdinalIgnoreCase)) return false;
        if (_activeFilter?.Family is { } family && !row.Family.Equals(family, StringComparison.OrdinalIgnoreCase)) return false;
        var search = SearchText.Text.Trim();
        return search.Length == 0 ||
               row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Type.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.TechnicalName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Library.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void LibraryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _activeFilter = e.NewValue as LibraryFilterNode;
        _rowsView?.Refresh();
    }

    private void SearchText_TextChanged(object sender, TextChangedEventArgs e) => _rowsView?.Refresh();

    private void ToggleSelectedRowsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = ComponentsGrid.SelectedItems.Cast<ComponentRowViewModel>().ToArray();
        if (selectedRows.Length == 0 && _rowsView is not null)
        {
            selectedRows = _rowsView.Cast<ComponentRowViewModel>().ToArray();
        }
        var shouldSelect = selectedRows.Any(row => !row.IsSelected);
        foreach (var row in selectedRows) row.IsSelected = shouldSelect;
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = false;
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComponentRowViewModel.IsSelected)) RefreshSelectionSummary();
    }

    private void RefreshSelectionSummary()
    {
        var selected = _rows.Where(row => row.IsSelected).ToArray();
        SelectedSummary.Text = $"{selected.Length:N0} .top · {selected.Sum(row => row.AssociatedCount):N0} fichier(s) associé(s)";
    }

    private void ComponentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComponentsGrid.SelectedItem is not ComponentRowViewModel row)
        {
            ClearPreview();
            return;
        }

        DetailName.Text = $"{row.Type} · {row.Name}";
        DetailPath.Text = row.Component.RelativeTopPath;
        AssociatedFilesList.ItemsSource = row.Component.AssociatedFiles.Select(Path.GetFileName).ToArray();
        var png = row.Component.AssociatedFiles.FirstOrDefault(path => path.EndsWith(".top.png", StringComparison.OrdinalIgnoreCase));
        if (png is null || !File.Exists(png))
        {
            ClearPreview(keepDetails: true);
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(png);
            bitmap.EndInit();
            PreviewImage.Source = bitmap;
            NoPreviewText.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ClearPreview(keepDetails: true);
        }
    }

    private void ClearPreview(bool keepDetails = false)
    {
        PreviewImage.Source = null;
        NoPreviewText.Visibility = Visibility.Visible;
        if (keepDetails) return;
        DetailName.Text = string.Empty;
        DetailPath.Text = string.Empty;
        AssociatedFilesList.ItemsSource = null;
    }

    private void VariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _rows.Clear();
        LibraryTree.Items.Clear();
        _scanResult = null;
        RefreshCounter();
        RefreshPackagePreview();
        RefreshSelectionSummary();
        StatusText.Text = "Variante modifiée : relance l’analyse de la source correspondante.";
    }

    private void PackageLabelText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RefreshPackagePreview();
    }

    private void RefreshCounter()
    {
        _nextCounter = NamingService.FindNextCounter(_settings.ProductArchivesPath, _settings.ProductStagingPath);
        CounterText.Text = _nextCounter.ToString("000");
    }

    private void RefreshPackagePreview()
    {
        try
        {
            PackageNamePreview.Text = NamingService.BuildProductPackageName(_nextCounter, PackageLabelText.Text, SelectedVariant);
        }
        catch
        {
            PackageNamePreview.Text = $"{_nextCounter:000}_Libellé_({SelectedVariant})";
        }
    }

    private void BuildReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null)
        {
            MessageBox.Show("Analyse d’abord la bibliothèque source.", "Analyse requise", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string packageName;
        try
        {
            packageName = NamingService.BuildProductPackageName(_nextCounter, PackageLabelText.Text, SelectedVariant);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Nom du package", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selected = _rows.Where(row => row.IsSelected).Select(row => row.Component).ToArray();
        var plan = _simulationService.BuildPlan(_scanResult, selected, _settings, SelectedVariant, packageName);
        var html = _reportService.BuildProductPackageReport(plan);
        new ReportWindow(html, $"Rapport_{packageName}.html") { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
