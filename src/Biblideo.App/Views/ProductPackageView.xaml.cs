using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
    private readonly ObservableCollection<LibraryFilterNode> _libraryNodes = [];
    private readonly Stack<HashSet<ComponentRowViewModel>> _selectionHistory = new();
    private ICollectionView? _rowsView;
    private LibraryScanResult? _scanResult;
    private LibraryVariant _selectedVariant = LibraryVariant.SC;
    private string? _activeLibrary;
    private string? _activeFamily;
    private string? _activeType;
    private int _nextCounter;
    private int _currentStep = 1;
    private bool _suppressSelectionSync;
    private bool _isBuildingFilters;
    private string? _lastReportHtml;
    private string? _lastReportFileName;

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
        RefreshVariantVisuals();
        SourcePathText.Text = _settings.GetSourcePath(_selectedVariant);
        LibraryList.ItemsSource = _libraryNodes;
        RefreshSelectionSummary();
        ShowStep(1);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPackageName(out _)) return;

        var source = _settings.GetSourcePath(_selectedVariant);
        IdentityScanProgress.Visibility = Visibility.Visible;
        IdentityStatusText.Text = $"Analyse en lecture seule : {source}";
        IsEnabled = false;
        try
        {
            _scanResult = await _scanner.ScanAsync(source);
            _rows.Clear();
            foreach (var component in _scanResult.Components)
            {
                _rows.Add(new ComponentRowViewModel(component));
            }

            _rowsView = CollectionViewSource.GetDefaultView(_rows);
            _rowsView.Filter = FilterRow;
            ComponentsList.ItemsSource = _rowsView;
            BuildLibraryList();
            SelectDefaultLibrary();
            BuildFamilyFilters();
            RefreshFilteredView();
            RefreshSelectionSummary();
            SelectionStatusText.Text = $"{_rows.Count:N0} composants analysés · Ctrl ou Maj pour sélectionner en masse · la sélection est conservée entre les bibliothèques.";
            IdentityStatusText.Text = $"{_rows.Count:N0} composants trouvés · {_scanResult.IgnoredBakCount:N0} .bak et {_scanResult.IgnoredCfgCount:N0} .cfg ignorés.";
            ShowStep(2);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Analyse impossible", MessageBoxButton.OK, MessageBoxImage.Error);
            IdentityStatusText.Text = "Analyse interrompue. Vérifie le chemin source dans les paramètres.";
        }
        finally
        {
            IsEnabled = true;
            IdentityScanProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildLibraryList()
    {
        _isBuildingFilters = true;
        try
        {
            _libraryNodes.Clear();
            _libraryNodes.Add(new LibraryFilterNode { Name = "Toutes", TotalCount = _rows.Count });
            foreach (var group in _rows.GroupBy(row => row.Library).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                _libraryNodes.Add(new LibraryFilterNode
                {
                    Name = group.Key,
                    Library = group.Key,
                    TotalCount = group.Count()
                });
            }
        }
        finally
        {
            _isBuildingFilters = false;
        }
        RefreshLibraryCounts();
    }

    private void SelectDefaultLibrary()
    {
        var preferred = _libraryNodes.FirstOrDefault(node =>
            node.Library?.Equals("_Ideo_Utilisateur", StringComparison.OrdinalIgnoreCase) == true);
        LibraryList.SelectedItem = preferred ?? _libraryNodes.FirstOrDefault();
        _activeLibrary = (LibraryList.SelectedItem as LibraryFilterNode)?.Library;
    }

    private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isBuildingFilters) return;
        _activeLibrary = (LibraryList.SelectedItem as LibraryFilterNode)?.Library;
        _activeFamily = null;
        _activeType = null;
        BuildFamilyFilters();
        RefreshFilteredView();
    }

    private void ResetLibraryFilterButton_Click(object sender, RoutedEventArgs e)
    {
        LibraryList.SelectedItem = _libraryNodes.FirstOrDefault();
    }

    private void BuildFamilyFilters()
    {
        _isBuildingFilters = true;
        try
        {
            FamilyFiltersPanel.Children.Clear();
            var families = RowsForActiveLibrary()
                .Select(row => row.Family)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AddFamilyButton("Toutes", null, _activeFamily is null);
            foreach (var family in families)
            {
                AddFamilyButton(family, family, family.Equals(_activeFamily, StringComparison.OrdinalIgnoreCase));
            }
            PopulateTypeFilter();
        }
        finally
        {
            _isBuildingFilters = false;
        }
    }

    private void AddFamilyButton(string label, string? family, bool active)
    {
        var button = new Button
        {
            Content = label,
            Tag = family,
            Style = (Style)FindResource("FilterButtonStyle")
        };
        if (active)
        {
            button.Background = (Brush)FindResource("AccentDarkBrush");
            button.BorderBrush = (Brush)FindResource("AccentBrush");
        }
        button.Click += FamilyFilterButton_Click;
        FamilyFiltersPanel.Children.Add(button);
    }

    private void FamilyFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        _activeFamily = button.Tag as string;
        _activeType = null;
        BuildFamilyFilters();
        RefreshFilteredView();
    }

    private void PopulateTypeFilter()
    {
        var types = RowsForActiveLibrary()
            .Where(row => _activeFamily is null || row.Family.Equals(_activeFamily, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Type)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        types.Insert(0, "Tous les types");
        TypeFilterCombo.ItemsSource = types;
        TypeFilterCombo.SelectedIndex = 0;
    }

    private void TypeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isBuildingFilters) return;
        _activeType = TypeFilterCombo.SelectedIndex <= 0 ? null : TypeFilterCombo.SelectedItem as string;
        RefreshFilteredView();
    }

    private IEnumerable<ComponentRowViewModel> RowsForActiveLibrary() =>
        _rows.Where(row => _activeLibrary is null || row.Library.Equals(_activeLibrary, StringComparison.OrdinalIgnoreCase));

    private bool FilterRow(object item)
    {
        if (item is not ComponentRowViewModel row) return false;
        if (_activeLibrary is not null && !row.Library.Equals(_activeLibrary, StringComparison.OrdinalIgnoreCase)) return false;
        if (_activeFamily is not null && !row.Family.Equals(_activeFamily, StringComparison.OrdinalIgnoreCase)) return false;
        if (_activeType is not null && !row.Type.Equals(_activeType, StringComparison.OrdinalIgnoreCase)) return false;
        var search = SearchText.Text.Trim();
        return search.Length == 0 ||
               row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Type.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Family.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.TechnicalName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               row.Library.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchText_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilteredView();

    private void RefreshFilteredView()
    {
        if (_rowsView is null) return;
        _suppressSelectionSync = true;
        try
        {
            _rowsView.Refresh();
            RestoreVisibleSelectionCore();
        }
        finally
        {
            _suppressSelectionSync = false;
        }

        var visible = _rowsView.Cast<ComponentRowViewModel>().ToArray();
        ActiveLibraryTitle.Text = _activeLibrary ?? "Toutes les bibliothèques";
        VisibleCountText.Text = $"{visible.Length:N0} composant(s) visible(s)" +
                                (_activeFamily is null ? string.Empty : $" · {_activeFamily}") +
                                (_activeType is null ? string.Empty : $" · {_activeType}");
    }

    private void RestoreVisibleSelectionCore()
    {
        ComponentsList.SelectedItems.Clear();
        if (_rowsView is null) return;
        foreach (var row in _rowsView.Cast<ComponentRowViewModel>().Where(item => item.IsSelected))
        {
            ComponentsList.SelectedItems.Add(row);
        }
    }

    private void ComponentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionSync && (e.AddedItems.Count > 0 || e.RemovedItems.Count > 0))
        {
            PushSelectionHistory();
            foreach (var removed in e.RemovedItems.OfType<ComponentRowViewModel>()) removed.IsSelected = false;
            foreach (var added in e.AddedItems.OfType<ComponentRowViewModel>()) added.IsSelected = true;
            RefreshSelectionSummary();
        }

        var preview = ComponentsList.SelectedItem as ComponentRowViewModel
                      ?? e.AddedItems.OfType<ComponentRowViewModel>().LastOrDefault()
                      ?? e.RemovedItems.OfType<ComponentRowViewModel>().LastOrDefault();
        if (preview is not null) UpdatePreview(preview);
    }

    private void PushSelectionHistory()
    {
        var snapshot = _rows.Where(row => row.IsSelected).ToHashSet();
        if (_selectionHistory.TryPeek(out var previous) && previous.SetEquals(snapshot)) return;
        _selectionHistory.Push(snapshot);
        UndoSelectionButton.IsEnabled = true;
    }

    private void UndoSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectionHistory.TryPop(out var previous)) return;
        _suppressSelectionSync = true;
        try
        {
            foreach (var row in _rows) row.IsSelected = previous.Contains(row);
            RestoreVisibleSelectionCore();
        }
        finally
        {
            _suppressSelectionSync = false;
        }
        UndoSelectionButton.IsEnabled = _selectionHistory.Count > 0;
        RefreshSelectionSummary();
    }

    private void SelectVisibleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rowsView is null) return;
        PushSelectionHistory();
        foreach (var row in _rowsView.Cast<ComponentRowViewModel>()) row.IsSelected = true;
        RestoreVisibleSelection();
        RefreshSelectionSummary();
    }

    private void ClearVisibleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rowsView is null) return;
        PushSelectionHistory();
        foreach (var row in _rowsView.Cast<ComponentRowViewModel>()) row.IsSelected = false;
        RestoreVisibleSelection();
        RefreshSelectionSummary();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.All(row => !row.IsSelected)) return;
        PushSelectionHistory();
        foreach (var row in _rows) row.IsSelected = false;
        RestoreVisibleSelection();
        RefreshSelectionSummary();
    }

    private void RemoveFromBasketButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedInBasket = SelectionBasket.SelectedItems.Cast<ComponentRowViewModel>().ToArray();
        if (selectedInBasket.Length == 0) return;
        PushSelectionHistory();
        foreach (var row in selectedInBasket) row.IsSelected = false;
        RestoreVisibleSelection();
        RefreshSelectionSummary();
    }

    private void RestoreVisibleSelection()
    {
        _suppressSelectionSync = true;
        try
        {
            RestoreVisibleSelectionCore();
        }
        finally
        {
            _suppressSelectionSync = false;
        }
    }

    private void RefreshSelectionSummary()
    {
        var selected = _rows.Where(row => row.IsSelected).ToArray();
        var componentFiles = selected.Sum(row => row.FileCount);
        SelectedSummary.Text = $"{selected.Length:N0} composant(s) dans le package · {componentFiles:N0} fichier(s) composant";
        if (_scanResult is null)
        {
            SelectionStatusText.Text = "Analyse la source pour commencer la sélection.";
        }
        SelectionBasket.ItemsSource = selected;
        VerifyButton.IsEnabled = selected.Length > 0;
        RefreshLibraryCounts();
    }

    private void RefreshLibraryCounts()
    {
        foreach (var node in _libraryNodes)
        {
            node.SelectedCount = _rows.Count(row => row.IsSelected &&
                (node.Library is null || row.Library.Equals(node.Library, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void UpdatePreview(ComponentRowViewModel row)
    {
        DetailName.Text = $"{row.Type} · {row.Name}";
        DetailPath.Text = row.Component.RelativeTopPath;
        AssociatedFilesList.ItemsSource = new[] { Path.GetFileName(row.Component.TopFilePath) }
            .Concat(row.Component.AssociatedFiles.Select(Path.GetFileName))
            .ToArray();
        PreviewImage.Source = row.Thumbnail;
        NoPreviewText.Visibility = row.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenBasketButton_Click(object sender, RoutedEventArgs e) => BasketDrawer.Visibility = Visibility.Visible;
    private void CloseBasketButton_Click(object sender, RoutedEventArgs e) => BasketDrawer.Visibility = Visibility.Collapsed;

    private void GoToVerificationButton_Click(object sender, RoutedEventArgs e) => TryShowVerification();

    private bool TryShowVerification()
    {
        if (_scanResult is null)
        {
            MessageBox.Show("Analyse d’abord la bibliothèque source.", "Analyse requise", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (_rows.All(row => !row.IsSelected))
        {
            MessageBox.Show("Ajoute au moins un composant au package.", "Package vide", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!TryGetPackageName(out _)) return false;
        RefreshVerification();
        ShowStep(3);
        return true;
    }

    private void RefreshVerification()
    {
        if (_scanResult is null || !TryGetPackageName(out var packageName)) return;
        var selected = _rows.Where(row => row.IsSelected).ToArray();
        var plan = _simulationService.BuildPlan(_scanResult, selected.Select(row => row.Component), _settings, _selectedVariant, packageName);

        VerificationComponentCount.Text = selected.Length.ToString("N0");
        VerificationFileCount.Text = plan.Files.Count.ToString("N0");
        VerificationLibraryCount.Text = selected.Select(row => row.Library).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0");
        VerificationWarningCount.Text = (plan.Warnings.Count + plan.BlockingErrors.Count).ToString("N0");
        VerificationStatusText.Text = plan.BlockingErrors.Count > 0
            ? $"{plan.BlockingErrors.Count:N0} erreur(s) bloquante(s) à corriger."
            : plan.Warnings.Count > 0
                ? $"Prêt pour la simulation avec {plan.Warnings.Count:N0} avertissement(s) détaillé(s) dans le rapport."
                : "Tous les contrôles sont au vert. Le package est prêt pour la simulation.";

        VerificationLibrariesGrid.ItemsSource = selected
            .GroupBy(row => row.Library, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var prefix = Path.Combine("Bibliotheque", group.Key) + Path.DirectorySeparatorChar;
                var fileCount = plan.Files.Count(file => file.RelativeDestination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                var warningCount = group.Sum(row => row.Component.Warnings.Count);
                return new PackageVerificationRow(
                    group.Key,
                    group.Count(),
                    fileCount,
                    warningCount == 0 ? "Prêt" : $"{warningCount} à contrôler");
            })
            .ToArray();
    }

    private void BackToSelectionButton_Click(object sender, RoutedEventArgs e) => ShowStep(2);

    private void BuildReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null || !TryGetPackageName(out var packageName)) return;
        var selected = _rows.Where(row => row.IsSelected).Select(row => row.Component).ToArray();
        var plan = _simulationService.BuildPlan(_scanResult, selected, _settings, _selectedVariant, packageName);
        _lastReportHtml = _reportService.BuildProductPackageReport(plan);
        _lastReportFileName = $"Rapport_{packageName}.html";
        LastReportNameText.Text = _lastReportFileName;
        ShowStep(4);
        OpenLastReport();
    }

    private void OpenLastReportButton_Click(object sender, RoutedEventArgs e) => OpenLastReport();

    private void OpenLastReport()
    {
        if (_lastReportHtml is null || _lastReportFileName is null) return;
        new ReportWindow(_lastReportHtml, _lastReportFileName) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void VariantScButton_Click(object sender, RoutedEventArgs e) => ChangeVariant(LibraryVariant.SC);
    private void VariantEpButton_Click(object sender, RoutedEventArgs e) => ChangeVariant(LibraryVariant.EP);

    private void ChangeVariant(LibraryVariant variant)
    {
        if (_selectedVariant == variant) return;
        if (_scanResult is not null)
        {
            var answer = MessageBox.Show(
                "Changer de variante effacera l’analyse et la sélection actuelles. Continuer ?",
                "Changer de variante",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            ResetScan();
        }
        _selectedVariant = variant;
        RefreshVariantVisuals();
        SourcePathText.Text = _settings.GetSourcePath(_selectedVariant);
        RefreshCounter();
        RefreshPackagePreview();
    }

    private void RefreshVariantVisuals()
    {
        var accent = (Brush)FindResource("AccentDarkBrush");
        var normal = (Brush)FindResource("SurfaceAltBrush");
        VariantScButton.Background = _selectedVariant == LibraryVariant.SC ? accent : normal;
        VariantEpButton.Background = _selectedVariant == LibraryVariant.EP ? accent : normal;
        VariantScButton.BorderBrush = _selectedVariant == LibraryVariant.SC ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
        VariantEpButton.BorderBrush = _selectedVariant == LibraryVariant.EP ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
    }

    private void ResetScan()
    {
        _scanResult = null;
        _rowsView = null;
        _rows.Clear();
        _libraryNodes.Clear();
        _selectionHistory.Clear();
        ComponentsList.ItemsSource = null;
        FamilyFiltersPanel.Children.Clear();
        TypeFilterCombo.ItemsSource = null;
        SelectionBasket.ItemsSource = null;
        SearchText.Text = string.Empty;
        BasketDrawer.Visibility = Visibility.Collapsed;
        _lastReportHtml = null;
        _lastReportFileName = null;
        ClearPreview();
        RefreshSelectionSummary();
        IdentityStatusText.Text = "Variante modifiée. Lance une nouvelle analyse de la source correspondante.";
        ShowStep(1);
    }

    private void ClearPreview()
    {
        PreviewImage.Source = null;
        NoPreviewText.Visibility = Visibility.Visible;
        DetailName.Text = "Sélectionne un composant";
        DetailPath.Text = string.Empty;
        AssociatedFilesList.ItemsSource = null;
    }

    private void PackageLabelText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) RefreshPackagePreview();
    }

    private void RefreshCounter()
    {
        _nextCounter = NamingService.FindNextCounter(_settings.ProductArchivesPath, _settings.ProductStagingPath);
        CounterText.Text = _nextCounter.ToString("000");
        CounterBadgeText.Text = $"COMPTEUR {_nextCounter:000}";
    }

    private void RefreshPackagePreview()
    {
        try
        {
            PackageNamePreview.Text = NamingService.BuildProductPackageName(_nextCounter, PackageLabelText.Text, _selectedVariant);
        }
        catch
        {
            PackageNamePreview.Text = $"{_nextCounter:000}_Libellé_({_selectedVariant})";
        }
    }

    private bool TryGetPackageName(out string packageName)
    {
        try
        {
            packageName = NamingService.BuildProductPackageName(_nextCounter, PackageLabelText.Text, _selectedVariant);
            return true;
        }
        catch (Exception exception)
        {
            packageName = string.Empty;
            MessageBox.Show(exception.Message, "Nom du package", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void Step1Button_Click(object sender, RoutedEventArgs e) => ShowStep(1);
    private void Step2Button_Click(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null)
        {
            MessageBox.Show("Lance d’abord l’analyse de la source.", "Analyse requise", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowStep(2);
    }
    private void Step3Button_Click(object sender, RoutedEventArgs e) => TryShowVerification();
    private void Step4Button_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReportHtml is null)
        {
            MessageBox.Show("Génère d’abord la simulation depuis l’étape de vérification.", "Rapport non généré", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowStep(4);
    }

    private void ShowStep(int step)
    {
        _currentStep = step;
        IdentificationPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        SelectionPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        VerificationPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        ReportPanel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        StepProgress.Value = step * 25;

        var active = (Brush)FindResource("AccentDarkBrush");
        var inactive = (Brush)FindResource("SurfaceAltBrush");
        var circleInactive = new SolidColorBrush(Color.FromRgb(36, 52, 77));
        var buttons = new[] { Step1Button, Step2Button, Step3Button, Step4Button };
        var circles = new[] { Step1Circle, Step2Circle, Step3Circle, Step4Circle };
        for (var index = 0; index < buttons.Length; index++)
        {
            var isActive = index + 1 == _currentStep;
            buttons[index].Background = isActive ? active : inactive;
            circles[index].Background = isActive ? active : circleInactive;
        }
    }
}
