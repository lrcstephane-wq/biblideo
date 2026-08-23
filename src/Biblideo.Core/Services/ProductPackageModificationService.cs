using System.Security.Cryptography;
using System.Text.Json;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class ProductPackageModificationService
{
    private readonly ProductPackageSimulationService _simulationService = new();
    private readonly ExistingProductPackageService _packageService = new();

    public ProductPackageModificationPlan BuildPlan(
        ExistingProductPackage package,
        LibraryScanResult scan,
        IEnumerable<ComponentDescriptor> selectedComponents,
        AppSettings settings,
        ExistingFileCopyMode copyMode)
    {
        ExistingProductPackageService.EnsureDirectChild(settings.ProductStagingPath, package.FullPath);
        if (package.Variant != (NamingService.TryParseProductPackage(package.Name, out _, out _, out var parsedVariant) ? parsedVariant : package.Variant))
            throw new InvalidOperationException("La variante du package est incohérente avec son nom.");

        var selected = selectedComponents.DistinctBy(component => component.RelativeTopPath, StringComparer.OrdinalIgnoreCase).ToArray();
        var sourcePrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scan.SourceRoot)) + Path.DirectorySeparatorChar;
        var sourceComponents = selected.Where(component =>
            Path.GetFullPath(component.TopFilePath).StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        var desiredPlan = _simulationService.BuildPlan(scan, sourceComponents, settings, package.Variant, package.Name);

        var original = package.ComponentRelativeTopPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedPaths = selected.Select(component => component.RelativeTopPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourcePaths = scan.Components.Select(component => component.RelativeTopPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = selectedPaths.Except(original, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = original.Except(selectedPaths, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var retained = original.Intersect(selectedPaths, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var orphans = original.Except(sourcePaths, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        var existingFiles = package.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredByDestination = desiredPlan.Files.ToDictionary(
            file => Normalize(file.RelativeDestination),
            file => file,
            StringComparer.OrdinalIgnoreCase);
        var actions = new List<PackageFileAction>();

        foreach (var desired in desiredByDestination.Values.OrderBy(file => file.RelativeDestination, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Normalize(desired.RelativeDestination);
            var destination = SafePackagePath(package.FullPath, relative);
            if (!existingFiles.Contains(relative))
            {
                actions.Add(new PackageFileAction(PackageFileActionKind.Add, relative, desired.SourcePath, destination, desired.Reason));
            }
            else if (copyMode == ExistingFileCopyMode.Overwrite)
            {
                actions.Add(new PackageFileAction(PackageFileActionKind.Overwrite, relative, desired.SourcePath, destination, desired.Reason));
            }
            else
            {
                actions.Add(new PackageFileAction(PackageFileActionKind.Preserve, relative, desired.SourcePath, destination, "Copie existante conservée à la demande de l’utilisateur"));
            }
        }

        var removableFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var removedTop in removed)
            removableFiles.UnionWith(_packageService.ResolvePackageBundle(package, removedTop).Select(Normalize));
        removableFiles.ExceptWith(desiredByDestination.Keys);
        foreach (var relative in removableFiles.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!existingFiles.Contains(relative)) continue;
            actions.Add(new PackageFileAction(
                PackageFileActionKind.Remove,
                relative,
                null,
                SafePackagePath(package.FullPath, relative),
                "Composant retiré de la sélection"));
        }

        var covered = actions.Select(action => action.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in existingFiles.Except(covered, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(new PackageFileAction(
                PackageFileActionKind.Unmanaged,
                relative,
                null,
                SafePackagePath(package.FullPath, relative),
                "Fichier existant non géré : laissé intact"));
        }

        var errors = new List<string>(desiredPlan.BlockingErrors);
        if (!Path.GetFullPath(desiredPlan.TargetRoot).Equals(Path.GetFullPath(package.FullPath), StringComparison.OrdinalIgnoreCase))
            errors.Add("La destination calculée ne correspond pas exactement au package ouvert.");
        var warnings = desiredPlan.Warnings.ToList();
        if (orphans.Length > 0)
            warnings.Add($"{orphans.Length} composant(s) du package sont absents de la bibliothèque source. Ils restent intacts tant qu’ils sont sélectionnés.");

        return new ProductPackageModificationPlan
        {
            ExistingPackage = package,
            CopyMode = copyMode,
            DesiredPlan = desiredPlan,
            AddedComponents = added,
            RemovedComponents = removed,
            RetainedComponents = retained,
            OrphanComponents = orphans,
            FileActions = actions,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            BlockingErrors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public async Task<ProductPackageModificationResult> ExecuteAsync(
        ProductPackageModificationPlan plan,
        AppSettings settings,
        IProgress<PackageCopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (plan.BlockingErrors.Count > 0)
            throw new InvalidOperationException("Le plan de modification contient des erreurs bloquantes.");
        ExistingProductPackageService.EnsureDirectChild(settings.ProductStagingPath, plan.ExistingPackage.FullPath);
        var currentFingerprint = _packageService.ComputeCurrentFingerprint(plan.ExistingPackage);
        if (!currentFingerprint.Equals(plan.ExistingPackage.Fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Le package a changé depuis son ouverture. Recharge-le avant de recommencer.");

        var actionable = plan.FileActions.Where(action => action.Kind is PackageFileActionKind.Add or PackageFileActionKind.Overwrite or PackageFileActionKind.Remove).ToArray();
        foreach (var action in actionable.Where(action => action.Kind is PackageFileActionKind.Add or PackageFileActionKind.Overwrite))
        {
            if (action.SourcePath is null || !File.Exists(action.SourcePath))
                throw new FileNotFoundException("Un fichier source a disparu depuis la simulation.", action.SourcePath);
        }

        var startedAt = DateTimeOffset.Now;
        var recoveryPath = Path.Combine(
            settings.ProductTrashPath,
            "Sauvegardes_Modifications",
            $"{DateTime.Now:yyyyMMdd_HHmmss}_{plan.ExistingPackage.Name}");
        var lockPath = Path.Combine(plan.ExistingPackage.FullPath, "biblideo.edit.lock");
        EnsureNoReparsePoint(plan.ExistingPackage.FullPath, lockPath);
        var lockStream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var results = new List<ModifiedFileResult>();
        try
        {
            var completed = 0;
            foreach (var action in actionable.Where(action => action.Kind is PackageFileActionKind.Add or PackageFileActionKind.Overwrite))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafePackagePath(plan.ExistingPackage.FullPath, action.RelativePath);
                EnsureNoReparsePoint(plan.ExistingPackage.FullPath, destination);
                var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Destination de fichier invalide.");
                Directory.CreateDirectory(parent);
                var temporary = destination + $".biblideo.{Guid.NewGuid():N}.tmp";
                try
                {
                    await CopyAndVerifyAsync(action.SourcePath!, temporary, cancellationToken);
                    if (action.Kind == PackageFileActionKind.Overwrite && File.Exists(destination))
                    {
                        var backup = SafePackagePath(recoveryPath, action.RelativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(destination, backup, overwrite: false);
                    }
                    File.Move(temporary, destination, action.Kind == PackageFileActionKind.Overwrite);
                    var info = new FileInfo(destination);
                    results.Add(new ModifiedFileResult(action.Kind, action.RelativePath, await ComputeSha256Async(destination, cancellationToken), info.Length));
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                completed++;
                progress?.Report(new PackageCopyProgress(completed, actionable.Length, action.RelativePath));
            }

            foreach (var action in actionable.Where(action => action.Kind == PackageFileActionKind.Remove))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = SafePackagePath(plan.ExistingPackage.FullPath, action.RelativePath);
                EnsureNoReparsePoint(plan.ExistingPackage.FullPath, destination);
                if (File.Exists(destination))
                {
                    var recovery = SafePackagePath(recoveryPath, action.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                    File.Move(destination, recovery, overwrite: false);
                    results.Add(new ModifiedFileResult(PackageFileActionKind.Remove, action.RelativePath, null, new FileInfo(recovery).Length));
                }
                completed++;
                progress?.Report(new PackageCopyProgress(completed, actionable.Length, action.RelativePath));
            }

            var manifestPath = Path.Combine(plan.ExistingPackage.FullPath, "biblideo.manifest.json");
            EnsureNoReparsePoint(plan.ExistingPackage.FullPath, manifestPath);
            if (File.Exists(manifestPath))
            {
                var manifestBackup = Path.Combine(recoveryPath, "biblideo.manifest.json");
                Directory.CreateDirectory(Path.GetDirectoryName(manifestBackup)!);
                File.Copy(manifestPath, manifestBackup, overwrite: false);
            }
            var manifest = new
            {
                schemaVersion = 2,
                packageName = plan.ExistingPackage.Name,
                variant = plan.ExistingPackage.Variant.ToString(),
                modifiedAt = DateTimeOffset.Now,
                copyMode = plan.CopyMode.ToString(),
                sourceRoot = plan.DesiredPlan.SourceRoot,
                recoveryPath,
                addedComponents = plan.AddedComponents,
                removedComponents = plan.RemovedComponents,
                retainedComponents = plan.RetainedComponents,
                files = results
            };
            var manifestTemp = manifestPath + $".{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(manifestTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, true))
                await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            File.Move(manifestTemp, manifestPath, true);

            return new ProductPackageModificationResult
            {
                PackageName = plan.ExistingPackage.Name,
                PackagePath = plan.ExistingPackage.FullPath,
                RecoveryPath = Directory.Exists(recoveryPath) ? recoveryPath : string.Empty,
                ManifestPath = manifestPath,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.Now,
                Files = results
            };
        }
        finally
        {
            await lockStream.DisposeAsync();
            if (File.Exists(lockPath)) File.Delete(lockPath);
        }
    }

    private static async Task CopyAndVerifyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            await input.CopyToAsync(output, 131072, cancellationToken);
        var sourceHash = await ComputeSha256Async(source, cancellationToken);
        var destinationHash = await ComputeSha256Async(destination, cancellationToken);
        if (!sourceHash.Equals(destinationHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"La vérification SHA-256 a échoué pour {Path.GetFileName(source)}.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string Normalize(string path) => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string SafePackagePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidOperationException("Chemin absolu refusé dans le plan de modification.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Une opération tente de sortir du package produit. Elle a été bloquée.");
        return full;
    }

    private static void EnsureNoReparsePoint(string packageRoot, string candidate)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var full = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(root, full);
        var current = root;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Lien ou raccourci de fichier refusé dans le package : {relative}");
        }
    }
}
