using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Biblideo.App.ViewModels;
using Biblideo.App.Services;
using Biblideo.Core.Models;
using Biblideo.Core.Services;
using Microsoft.Win32;

namespace Biblideo.App.Views;

public partial class ProductPackageView : UserControl
{
    private readonly AppSettings _settings;
    private readonly LibraryScanner _scanner = new();
    private readonly ProductPackageSimulationService _simulationService = new();
    private readonly ProductPackageExecutionService _executionService = new();
    private readonly HtmlReportService _reportService = new();
    private ObservableCollection<ComponentRowViewModel> _rows = [];
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
    private bool _isInitializingIdentity;
    private string? _lastReportHtml;
    private string? _lastReportFileName;
    private bool _isMosaicView;

    public ProductPackageView(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Loaded += ProductPackageView_Loaded;
    }

    private void ProductPackageView_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializingIdentity = true;
        RefreshCounter();
        RefreshPackagePreview();
        RefreshVariantVisuals();
        SourcePathText.Text = _settings.GetSourcePath(_selectedVariant);
        LibraryList.ItemsSource = _libraryNodes;
        ApplyComponentView(false);
        RefreshSelectionSummary();
        ShowStep(1);
        _isInitializingIdentity = false;
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPackageName(out _)) return;

        var source = SourcePathText.Text.Trim();
        if (!Directory.Exists(source))
        {
            IdeoDialog.Warning(
                "Source inaccessible",
                $"Le dossier source est introuvable.\n\n{source}\n\nModifie le chemin ou utilise le bouton Parcourir.",
                "Même les engrenages ont besoin de savoir où ils vont.");
            return;
        }
        IdentityScanProgress.Visibility = Visibility.Visible;
        IdentityStatusText.Text = $"Analyse en lecture seule : {source}";
        IsEnabled = false;
        try
        {
            _scanResult = await _scanner.ScanAsync(source);
            ComponentsList.ItemsSource = null;
            _rowsView = null;
            _rows = new ObservableCollection<ComponentRowViewModel>(
                _scanResult.Components.Select(component => new ComponentRowViewModel(component)));

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
            var logPath = TryWriteScanLog(exception);
            CrashLogService.TryOpen(logPath);
            IdeoDialog.Error(
                "Analyse impossible",
                $"L’analyse n’a pas pu être terminée.\n\n{exception.Message}\n\nDétail technique :\n{logPath}");
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
            button.Background = new SolidColorBrush(Color.FromRgb(45, 76, 112));
            button.BorderBrush = (Brush)FindResource("AccentBrush");
            button.Foreground = new SolidColorBrush(Color.FromRgb(187, 215, 247));
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

    private void ListViewButton_Click(object sender, RoutedEventArgs e) => ApplyComponentView(false);

    private void MosaicViewButton_Click(object sender, RoutedEventArgs e)
        => ApplyComponentView(true);

    private void ApplyComponentView(bool mosaic)
    {
        _isMosaicView = mosaic;
        ComponentsList.ItemTemplate = (DataTemplate)FindResource(mosaic ? "ComponentMosaicTemplate" : "ComponentListTemplate");
        ComponentsList.ItemContainerStyle = (Style)FindResource(mosaic ? "ComponentMosaicItemStyle" : "ComponentListItemStyle");
        ComponentsList.ItemsPanel = (ItemsPanelTemplate)FindResource(mosaic ? "ComponentMosaicItemsPanel" : "ComponentListItemsPanel");
        ScrollViewer.SetCanContentScroll(ComponentsList, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(ComponentsList, ScrollBarVisibility.Disabled);

        var active = (Brush)FindResource("AccentDarkBrush");
        var inactive = (Brush)FindResource("SurfaceAltBrush");
        ListViewButton.Background = mosaic ? inactive : active;
        MosaicViewButton.Background = mosaic ? active : inactive;
        ListViewButton.Foreground = mosaic ? (Brush)FindResource("TextBrush") : Brushes.White;
        MosaicViewButton.Foreground = mosaic ? Brushes.White : (Brush)FindResource("TextBrush");
        RestoreVisibleSelection();
    }

    private void ClearVisibleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rowsView is null) return;
        var visibleSelected = _rowsView.Cast<ComponentRowViewModel>().Where(row => row.IsSelected).ToArray();
        if (visibleSelected.Length == 0)
        {
            SelectionStatusText.Text = "Aucun composant affiché ne faisait partie du package.";
            return;
        }
        PushSelectionHistory();
        foreach (var row in visibleSelected) row.IsSelected = false;
        RestoreVisibleSelection();
        RefreshSelectionSummary();
        SelectionStatusText.Text = $"{visibleSelected.Length:N0} composant(s) affiché(s) retiré(s) du package.";
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

    private void RemoveBasketItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ComponentRowViewModel row }) return;
        PushSelectionHistory();
        row.IsSelected = false;
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
        var selected = _rows.Where(row => row.IsSelected)
            .OrderBy(row => row.Library, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var componentFiles = selected.Sum(row => row.FileCount);
        SelectedSummary.Text = $"{selected.Length:N0} composant(s) dans le package · {componentFiles:N0} fichier(s) composant";
        BasketSummaryText.Text = $"{selected.Length:N0} composant(s) · {componentFiles:N0} fichier(s), regroupés par bibliothèque";
        if (_scanResult is null)
        {
            SelectionStatusText.Text = "Analyse la source pour commencer la sélection.";
        }
        var basketView = CollectionViewSource.GetDefaultView(selected);
        basketView.GroupDescriptions.Clear();
        basketView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ComponentRowViewModel.Library)));
        SelectionBasket.ItemsSource = basketView;
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
            IdeoDialog.Info("Analyse requise", "Analyse d’abord la bibliothèque source.", "Sans matière première, même Thomas ne fabrique pas de composant.");
            return false;
        }
        if (!Path.TrimEndingDirectorySeparator(_scanResult.SourceRoot).Equals(
                Path.TrimEndingDirectorySeparator(SourcePathText.Text.Trim()),
                StringComparison.OrdinalIgnoreCase))
        {
            IdeoDialog.Info("Nouvelle analyse requise", "Le chemin source a changé depuis la dernière analyse. Relance l’analyse avant de continuer.");
            return false;
        }
        if (_rows.All(row => !row.IsSelected))
        {
            IdeoDialog.Info("Package vide", "Ajoute au moins un composant au package.", "Un package vide, c’est très léger… mais assez peu utile.");
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
        VerificationTargetPathText.Text = plan.TargetRoot;
        VerificationStatusText.Text = plan.BlockingErrors.Count > 0
            ? $"{plan.BlockingErrors.Count:N0} erreur(s) bloquante(s) à corriger."
            : plan.Warnings.Count > 0
                ? $"Prêt pour la simulation avec {plan.Warnings.Count:N0} avertissement(s) détaillé(s) dans le rapport."
                : "Tous les contrôles sont au vert. Le package est prêt pour la simulation.";

        VerificationTree.ItemsSource = BuildVerificationTree(selected, plan);
    }

    private IReadOnlyList<VerificationTreeNode> BuildVerificationTree(
        IReadOnlyList<ComponentRowViewModel> selected,
        SimulationPlan plan)
    {
        var roots = new List<VerificationTreeNode>();
        var modelFiles = plan.Files
            .Where(file => file.Reason.StartsWith("Fichier du modèle", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativeDestination, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (modelFiles.Length > 0)
        {
            var modelRoot = new VerificationTreeNode
            {
                Name = "Structure et documents du modèle",
                Detail = $"{modelFiles.Length:N0} fichier(s) repris du modèle {_selectedVariant}",
                Glyph = "◆",
                Badge = "MODÈLE"
            };
            foreach (var file in modelFiles) AddPathToTree(modelRoot, file.RelativeDestination, file.Reason);
            roots.Add(modelRoot);
        }

        foreach (var libraryGroup in selected
                     .GroupBy(row => row.Library, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var libraryPrefix = Path.Combine("Bibliotheque", libraryGroup.Key) + Path.DirectorySeparatorChar;
            var libraryRoot = new VerificationTreeNode
            {
                Name = libraryGroup.Key,
                Detail = $"{libraryGroup.Count():N0} composant(s)",
                Glyph = "▣",
                Badge = "BIBLIOTHÈQUE",
                IsExpanded = true
            };

            foreach (var row in libraryGroup
                         .OrderBy(item => item.Family, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var componentNode = new VerificationTreeNode
                {
                    Name = $"{row.Type} · {row.Name}",
                    Detail = $"{row.Family} · {row.FileCount:N0} fichier(s)",
                    Glyph = "▦",
                    Badge = "COMPOSANT",
                    Component = row
                };
                var componentSources = new[] { row.Component.TopFilePath }.Concat(row.Component.AssociatedFiles);
                foreach (var source in componentSources)
                {
                    var planned = plan.Files.FirstOrDefault(file =>
                        file.SourcePath.Equals(source, StringComparison.OrdinalIgnoreCase));
                    componentNode.Children.Add(new VerificationTreeNode
                    {
                        Name = Path.GetFileName(source),
                        Detail = planned?.RelativeDestination ?? Path.Combine("Bibliotheque", Path.GetRelativePath(_scanResult!.SourceRoot, source)),
                        Glyph = "•",
                        Badge = GetFileBadge(source)
                    });
                }
                libraryRoot.Children.Add(componentNode);
            }

            var automaticFiles = plan.Files
                .Where(file => file.Reason.StartsWith("Icône automatique", StringComparison.OrdinalIgnoreCase) &&
                               file.RelativeDestination.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.RelativeDestination, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (automaticFiles.Length > 0)
            {
                var automaticNode = new VerificationTreeNode
                {
                    Name = "Icônes automatiques",
                    Detail = $"{automaticFiles.Length:N0} fichier(s) icn ajouté(s) automatiquement",
                    Glyph = "◇",
                    Badge = "ICÔNES"
                };
                foreach (var file in automaticFiles)
                {
                    automaticNode.Children.Add(new VerificationTreeNode
                    {
                        Name = Path.GetFileName(file.RelativeDestination),
                        Detail = file.RelativeDestination,
                        Glyph = "•",
                        Badge = "ICN"
                    });
                }
                libraryRoot.Children.Add(automaticNode);
            }
            roots.Add(libraryRoot);
        }
        return roots;
    }

    private static void AddPathToTree(VerificationTreeNode root, string relativePath, string detail)
    {
        var segments = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var isFile = index == segments.Length - 1;
            var existing = current.Children.FirstOrDefault(node =>
                node.Name.Equals(segments[index], StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new VerificationTreeNode
                {
                    Name = segments[index],
                    Detail = isFile ? detail : string.Empty,
                    Glyph = isFile ? "•" : "□",
                    Badge = isFile ? GetFileBadge(segments[index]) : "DOSSIER"
                };
                current.Children.Add(existing);
            }
            current = existing;
        }
    }

    private static string GetFileBadge(string path)
    {
        if (path.EndsWith(".top.png", StringComparison.OrdinalIgnoreCase)) return "PNG";
        var extension = Path.GetExtension(path).TrimStart('.');
        return string.IsNullOrWhiteSpace(extension) ? "FICHIER" : extension.ToUpperInvariant();
    }

    private void RemoveVerificationComponentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ComponentRowViewModel row }) return;
        PushSelectionHistory();
        row.IsSelected = false;
        RestoreVisibleSelection();
        RefreshSelectionSummary();
        if (_rows.Any(item => item.IsSelected))
        {
            RefreshVerification();
        }
        else
        {
            ShowStep(2);
            IdeoDialog.Info("Package vide", "Le package est maintenant vide. Ajoute au moins un composant pour poursuivre.", "Le dernier composant vient d’éteindre la lumière en partant.");
        }
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
        RealCopyStatusText.Text = plan.BlockingErrors.Count > 0
            ? "La copie réelle reste verrouillée tant que les erreurs bloquantes ne sont pas corrigées."
            : $"Prêt à créer {plan.Files.Count:N0} fichier(s) dans un nouveau dossier. Aucun écrasement autorisé.";
        RealCopyProgress.Visibility = Visibility.Collapsed;
        RealCopyProgress.Value = 0;
        CreateRealPackageButton.IsEnabled = plan.BlockingErrors.Count == 0;
        ShowStep(4);
        OpenLastReport();
    }

    private async void CreateRealPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null || !TryGetPackageName(out var packageName)) return;

        var selected = _rows.Where(row => row.IsSelected).Select(row => row.Component).ToArray();
        var plan = _simulationService.BuildPlan(_scanResult, selected, _settings, _selectedVariant, packageName);
        if (plan.BlockingErrors.Count > 0)
        {
            IdeoDialog.Error("Copie verrouillée", string.Join(Environment.NewLine, plan.BlockingErrors), "On corrige d’abord, on copie ensuite. Les dossiers clients apprécient cette philosophie.");
            return;
        }

        if (Directory.Exists(plan.TargetRoot) || File.Exists(plan.TargetRoot))
        {
            IdeoDialog.Warning(
                "Destination déjà occupée",
                $"Le dossier existe déjà :\n{plan.TargetRoot}\n\nBiblidéo refuse tout écrasement. Modifie le compteur ou le libellé.",
                "Même le bouton bleu n’a pas le droit d’écraser un collègue.");
            return;
        }

        var confirmed = IdeoDialog.Confirm(
            "Créer réellement le package ?",
            $"{plan.Files.Count:N0} fichier(s) seront COPIÉS dans :\n{plan.TargetRoot}\n\nLa source restera intacte. Aucun fichier ne sera supprimé, déplacé ou écrasé.",
            "Simulation validée, ceinture attachée, destination libre.",
            "Créer le package");
        if (!confirmed) return;

        CreateRealPackageButton.IsEnabled = false;
        RealCopyProgress.Visibility = Visibility.Visible;
        RealCopyProgress.Value = 0;
        RealCopyStatusText.Text = "Préparation de la copie contrôlée…";
        var completed = false;

        try
        {
            var progress = new Progress<PackageCopyProgress>(item =>
            {
                RealCopyProgress.Value = item.Percentage;
                RealCopyStatusText.Text = $"{item.CompletedFiles:N0} / {item.TotalFiles:N0} · {item.RelativeDestination}";
            });
            var result = await _executionService.ExecuteAsync(plan, progress);
            _lastReportHtml = _reportService.BuildProductPackageExecutionReport(plan, result);
            _lastReportFileName = $"Rapport_Copie_{packageName}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            LastReportNameText.Text = _lastReportFileName;

            string? savedReportPath = null;
            try
            {
                Directory.CreateDirectory(_settings.ReportsPath);
                savedReportPath = Path.Combine(_settings.ReportsPath, _lastReportFileName);
                await File.WriteAllTextAsync(savedReportPath, _lastReportHtml);
            }
            catch (Exception reportException)
            {
                IdeoDialog.Warning(
                    "Package créé, rapport réseau indisponible",
                    $"Le package est bien créé et vérifié, mais le rapport n’a pas pu être enregistré dans le dossier partagé.\n\n{reportException.Message}",
                    "Le package va bien. C’est seulement le rapport qui cherche encore le réseau.");
            }

            RealCopyProgress.Value = 100;
            RealCopyStatusText.Text = savedReportPath is null
                ? $"Package créé et vérifié : {result.Files.Count:N0} fichier(s). Rapport affiché uniquement."
                : $"Package créé et vérifié : {result.Files.Count:N0} fichier(s). Rapport enregistré dans le dossier partagé.";
            completed = true;

            IdeoDialog.Info(
                "Package créé et contrôlé",
                $"{result.Files.Count:N0} fichier(s) ont été copiés puis vérifiés par SHA-256.\n\nDestination :\n{result.TargetRoot}",
                "Aucun fichier source n’a été maltraité pendant cette opération.");
            OpenLastReport();
        }
        catch (Exception exception)
        {
            var logPath = TryWriteScanLog(exception);
            CrashLogService.TryOpen(logPath);
            RealCopyStatusText.Text = "Copie interrompue. Le dossier partiel est conservé pour diagnostic ; les sources sont intactes.";
            IdeoDialog.Error(
                "Copie interrompue",
                $"{exception.Message}\n\nLes sources n’ont pas été modifiées. Par sécurité, un éventuel dossier partiel n’est jamais supprimé automatiquement.\n\nRapport technique : {logPath}",
                "On garde les preuves, on ne balaie rien sous le tapis.");
        }
        finally
        {
            CreateRealPackageButton.IsEnabled = !completed;
        }
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
            if (!IdeoDialog.Confirm(
                    "Changer de variante",
                    "Changer de variante effacera l’analyse et la sélection actuelles.",
                    "Les fichiers source, eux, ne bougeront pas d’un millimètre.",
                    "Changer de variante")) return;
            ResetScan();
        }
        _selectedVariant = variant;
        RefreshVariantVisuals();
        SourcePathText.Text = _settings.GetSourcePath(_selectedVariant);
        RefreshCounter();
        RefreshPackagePreview();
    }

    private void CounterText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializingIdentity) return;
        if (int.TryParse(CounterText.Text.Trim(), out var counter) && counter is >= 0 and <= 999)
        {
            _nextCounter = counter;
            CounterText.ClearValue(Control.BorderBrushProperty);
            CounterBadgeText.Text = $"COMPTEUR {_nextCounter:000}";
            RefreshPackagePreview();
            return;
        }

        CounterText.BorderBrush = Brushes.OrangeRed;
        PackageNamePreview.Text = "Compteur invalide · valeur attendue de 000 à 999";
    }

    private void SourcePathText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializingIdentity || !IsLoaded) return;
        IdentityStatusText.Text = Directory.Exists(SourcePathText.Text.Trim())
            ? "Source accessible · prête pour une nouvelle analyse."
            : "Source introuvable · corrige le chemin avant l’analyse.";
    }

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var current = SourcePathText.Text.Trim();
        var dialog = new OpenFolderDialog
        {
            Title = $"Choisir la bibliothèque source {_selectedVariant}",
            InitialDirectory = Directory.Exists(current) ? current : string.Empty
        };
        if (dialog.ShowDialog() == true) SourcePathText.Text = dialog.FolderName;
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var source = SourcePathText.Text.Trim();
        if (!Directory.Exists(source))
        {
            IdeoDialog.Warning("Source inaccessible", "Le dossier source indiqué n’existe pas ou n’est pas accessible.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{source}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            IdeoDialog.Warning("Ouverture impossible", exception.Message);
        }
    }

    private void RefreshVariantVisuals()
    {
        var accent = (Brush)FindResource("AccentDarkBrush");
        var normal = (Brush)FindResource("SurfaceAltBrush");
        var normalText = (Brush)FindResource("TextBrush");
        VariantScButton.Background = _selectedVariant == LibraryVariant.SC ? accent : normal;
        VariantEpButton.Background = _selectedVariant == LibraryVariant.EP ? accent : normal;
        VariantScButton.Foreground = _selectedVariant == LibraryVariant.SC ? Brushes.White : normalText;
        VariantEpButton.Foreground = _selectedVariant == LibraryVariant.EP ? Brushes.White : normalText;
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
        try
        {
            _nextCounter = NamingService.FindNextCounter(_settings.ProductArchivesPath, _settings.ProductStagingPath);
            CounterText.Text = _nextCounter.ToString("000");
            CounterBadgeText.Text = $"COMPTEUR {_nextCounter:000}";
        }
        catch (Exception exception)
        {
            _nextCounter = 1;
            CounterText.Text = "001";
            CounterBadgeText.Text = "COMPTEUR 001 · À VÉRIFIER";
            IdentityStatusText.Text = $"Compteur automatique indisponible : {exception.Message}";
        }
    }

    private void RefreshPackagePreview()
    {
        try
        {
            if (IsLoaded && (!int.TryParse(CounterText.Text.Trim(), out _nextCounter) || _nextCounter is < 0 or > 999))
            {
                PackageNamePreview.Text = "Compteur invalide · valeur attendue de 000 à 999";
                return;
            }
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
            if (!int.TryParse(CounterText.Text.Trim(), out _nextCounter) || _nextCounter is < 0 or > 999)
            {
                throw new ArgumentException("Le compteur doit être un nombre compris entre 000 et 999.");
            }
            packageName = NamingService.BuildProductPackageName(_nextCounter, PackageLabelText.Text, _selectedVariant);
            return true;
        }
        catch (Exception exception)
        {
            packageName = string.Empty;
            IdeoDialog.Warning("Nom du package", exception.Message);
            return false;
        }
    }

    private static string TryWriteScanLog(Exception exception)
    {
        try
        {
            return CrashLogService.Write("Analyse de bibliothèque", exception);
        }
        catch
        {
            return "Journal local indisponible";
        }
    }

    private void Step1Button_Click(object sender, RoutedEventArgs e) => ShowStep(1);
    private void Step2Button_Click(object sender, RoutedEventArgs e)
    {
        if (_scanResult is null)
        {
            IdeoDialog.Info("Analyse requise", "Lance d’abord l’analyse de la source.");
            return;
        }
        ShowStep(2);
    }
    private void Step3Button_Click(object sender, RoutedEventArgs e) => TryShowVerification();
    private void Step4Button_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReportHtml is null)
        {
            IdeoDialog.Info("Rapport non généré", "Génère d’abord la simulation depuis l’étape de vérification.");
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

        var active = new SolidColorBrush(Color.FromRgb(59, 70, 84));
        var inactive = (Brush)FindResource("SurfaceBrush");
        var circleActive = (Brush)FindResource("AccentDarkBrush");
        var circleInactive = new SolidColorBrush(Color.FromRgb(75, 85, 99));
        var buttons = new[] { Step1Button, Step2Button, Step3Button, Step4Button };
        var circles = new[] { Step1Circle, Step2Circle, Step3Circle, Step4Circle };
        for (var index = 0; index < buttons.Length; index++)
        {
            var isActive = index + 1 == _currentStep;
            buttons[index].Background = isActive ? active : inactive;
            circles[index].Background = isActive ? circleActive : circleInactive;
            if (circles[index].Child is TextBlock circleText)
            {
                circleText.Foreground = isActive ? Brushes.White : (Brush)FindResource("MutedBrush");
            }
        }
    }
}
