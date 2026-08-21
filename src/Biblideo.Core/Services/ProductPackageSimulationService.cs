using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class ProductPackageSimulationService
{
    public SimulationPlan BuildPlan(
        LibraryScanResult scan,
        IEnumerable<ComponentDescriptor> selectedComponents,
        AppSettings settings,
        LibraryVariant variant,
        string packageName)
    {
        var selected = selectedComponents.DistinctBy(item => item.TopFilePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var targetRoot = Path.Combine(settings.ProductStagingPath, packageName);
        var files = new Dictionary<string, PlannedFile>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>(scan.Warnings);
        var errors = new List<string>();

        if (selected.Length == 0)
        {
            errors.Add("Aucun composant .top n’est sélectionné.");
        }

        if (!NamingService.TryParseProductPackage(packageName, out _, out _, out var parsedVariant) || parsedVariant != variant)
        {
            errors.Add("Le nom du package produit ne correspond pas à la variante choisie.");
        }

        if (!Directory.Exists(settings.GetModelPath(variant)))
        {
            warnings.Add($"Modèle introuvable : {settings.GetModelPath(variant)}");
        }

        foreach (var component in selected)
        {
            AddFile(component.TopFilePath, "Composant .top");
            foreach (var associated in component.AssociatedFiles)
            {
                AddFile(associated, "Fichier associé au .top");
            }
            warnings.AddRange(component.Warnings.Select(warning => $"{component.RelativeTopPath} — {warning}"));
        }

        foreach (var library in selected.Select(item => item.LibraryName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!scan.AutomaticAssetsByLibrary.TryGetValue(library, out var assets))
            {
                continue;
            }
            foreach (var asset in assets)
            {
                AddFile(asset, "Icône automatique de la bibliothèque sélectionnée");
            }
        }

        var planned = files.Values.OrderBy(item => item.RelativeDestination, StringComparer.OrdinalIgnoreCase).ToArray();
        return new SimulationPlan
        {
            Variant = variant,
            OperationName = $"Constitution simulée de {packageName}",
            SourceRoot = scan.SourceRoot,
            TargetRoot = targetRoot,
            Files = planned,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            BlockingErrors = errors,
            TotalBytes = planned.Where(item => File.Exists(item.SourcePath)).Sum(item => new FileInfo(item.SourcePath).Length)
        };

        void AddFile(string sourcePath, string reason)
        {
            var relative = Path.GetRelativePath(scan.SourceRoot, sourcePath);
            var destination = Path.Combine("Bibliotheque", relative);
            if (!files.TryAdd(destination, new PlannedFile(sourcePath, destination, reason)))
            {
                warnings.Add($"Destination dupliquée détectée : {destination}");
            }
        }
    }
}
