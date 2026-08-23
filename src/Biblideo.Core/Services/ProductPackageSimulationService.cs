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
        var directories = new Dictionary<string, PlannedDirectory>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>(scan.Warnings);
        var errors = new List<string>();
        NamingService.TryParseProductPackage(packageName, out var counter, out _, out _);

        if (selected.Length == 0)
        {
            errors.Add("Aucun composant .top n’est sélectionné.");
        }

        if (!NamingService.TryParseProductPackage(packageName, out _, out _, out var parsedVariant) || parsedVariant != variant)
        {
            errors.Add("Le nom du package produit ne correspond pas à la variante choisie.");
        }

        var modelRoot = settings.GetModelPath(variant);
        if (!Directory.Exists(modelRoot))
        {
            warnings.Add($"Modèle introuvable : {modelRoot}");
        }
        else
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(modelRoot, "*", SearchOption.AllDirectories))
                {
                    var relative = ReplaceCounter(Path.GetRelativePath(modelRoot, directory));
                    directories[relative] = new PlannedDirectory(relative, $"Structure du modèle {variant}");
                }
                foreach (var modelFile in Directory.EnumerateFiles(modelRoot, "*", SearchOption.AllDirectories))
                {
                    var destination = ReplaceCounter(Path.GetRelativePath(modelRoot, modelFile));
                    AddPlannedFile(modelFile, destination, $"Fichier du modèle {variant}", replaceExisting: false);
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Modèle partiellement inaccessible : {exception.Message}");
            }
        }

        foreach (var component in selected)
        {
            AddSourceFile(component.TopFilePath, "Composant .top");
            foreach (var associated in component.AssociatedFiles)
            {
                AddSourceFile(associated, "Fichier associé au .top");
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
                AddSourceFile(asset, "Icône automatique de la bibliothèque sélectionnée");
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
            Directories = directories.Values.OrderBy(item => item.RelativeDestination, StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            BlockingErrors = errors,
            TotalBytes = planned.Where(item => File.Exists(item.SourcePath)).Sum(item => new FileInfo(item.SourcePath).Length)
        };

        void AddSourceFile(string sourcePath, string reason)
        {
            var relative = Path.GetRelativePath(scan.SourceRoot, sourcePath);
            var destination = Path.Combine("Bibliotheque", relative);
            AddPlannedFile(sourcePath, destination, reason, replaceExisting: true);
        }

        void AddPlannedFile(string sourcePath, string destination, string reason, bool replaceExisting)
        {
            var parent = Path.GetDirectoryName(destination);
            while (!string.IsNullOrWhiteSpace(parent))
            {
                directories.TryAdd(parent, new PlannedDirectory(parent, "Dossier nécessaire au package"));
                parent = Path.GetDirectoryName(parent);
            }

            var plannedFile = new PlannedFile(sourcePath, destination, reason);
            if (files.TryAdd(destination, plannedFile)) return;
            if (replaceExisting)
            {
                files[destination] = plannedFile;
                warnings.Add($"Le fichier issu de la bibliothèque remplacera celui du modèle : {destination}");
                return;
            }
            warnings.Add($"Destination dupliquée détectée dans le modèle : {destination}");
        }

        string ReplaceCounter(string relativePath) => relativePath.Replace("---", counter.ToString("000"), StringComparison.Ordinal);
    }
}
