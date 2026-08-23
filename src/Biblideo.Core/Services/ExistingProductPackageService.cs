using System.Security.Cryptography;
using System.Text;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class ExistingProductPackageService
{
    public Task<ExistingPackageCatalog> ScanAsync(string stagingRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(stagingRoot, cancellationToken), cancellationToken);

    public ExistingPackageCatalog Scan(string stagingRoot, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(stagingRoot))
            throw new DirectoryNotFoundException($"Le dossier des packages à déployer est introuvable : {stagingRoot}");

        var root = Path.GetFullPath(stagingRoot);
        var packages = new List<ExistingProductPackage>();
        var warnings = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!NamingService.TryParseProductPackage(name, out var counter, out var label, out var variant))
            {
                warnings.Add($"Dossier ignoré car son nom n’est pas conforme : {name}");
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add($"Lien de dossier ignoré : {name}");
                    continue;
                }

                var files = EnumeratePackageFiles(directory, cancellationToken)
                    .Select(path => Path.GetRelativePath(directory, path))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var libraryRoot = Path.Combine(directory, "Bibliotheque");
                var tops = files
                    .Where(relative => IsUnderLibrary(relative) && Path.GetExtension(relative).Equals(".top", StringComparison.OrdinalIgnoreCase))
                    .Select(relative => Path.GetRelativePath("Bibliotheque", relative))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                packages.Add(new ExistingProductPackage
                {
                    Name = name,
                    FullPath = Path.GetFullPath(directory),
                    Variant = variant,
                    Counter = counter,
                    Label = label,
                    LastModifiedAt = Directory.GetLastWriteTime(directory),
                    Fingerprint = ComputeFingerprint(directory, files),
                    Files = files,
                    ComponentRelativeTopPaths = tops,
                    HasManifest = File.Exists(Path.Combine(directory, "biblideo.manifest.json"))
                });
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Package inaccessible ignoré : {name} ({exception.Message})");
            }
        }

        return new ExistingPackageCatalog
        {
            Packages = packages
                .OrderByDescending(package => package.Counter)
                .ThenBy(package => package.Variant)
                .ToArray(),
            Warnings = warnings
        };
    }

    public string ComputeCurrentFingerprint(ExistingProductPackage package)
    {
        var files = EnumeratePackageFiles(package.FullPath, CancellationToken.None)
            .Select(path => Path.GetRelativePath(package.FullPath, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ComputeFingerprint(package.FullPath, files);
    }

    public ComponentDescriptor BuildPackageComponentDescriptor(ExistingProductPackage package, string relativeTopPath)
    {
        var topPath = Path.Combine(package.FullPath, "Bibliotheque", relativeTopPath);
        var technicalName = Path.GetFileNameWithoutExtension(topPath);
        var classified = ComponentNameParser.TryParse(technicalName, out var parsed);
        var segments = relativeTopPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var associated = ResolvePackageBundle(package, relativeTopPath)
            .Where(relative => !relative.Equals(Path.Combine("Bibliotheque", relativeTopPath), StringComparison.OrdinalIgnoreCase))
            .Select(relative => Path.Combine(package.FullPath, relative))
            .Where(File.Exists)
            .ToArray();
        return new ComponentDescriptor
        {
            TopFilePath = topPath,
            RelativeTopPath = relativeTopPath,
            LibraryName = segments.Length > 1 ? segments[0] : "Racine",
            FamilyName = segments.Length > 2 ? segments[1] : "Racine",
            TypeName = parsed?.Type ?? "Non classés",
            ComponentName = parsed?.Variant ?? technicalName,
            TechnicalName = technicalName,
            IsClassified = classified,
            AssociatedFiles = associated,
            Warnings = ["Composant absent de la bibliothèque source : copie actuelle conservée tant qu’il reste sélectionné."]
        };
    }

    public IReadOnlyList<string> ResolvePackageBundle(ExistingProductPackage package, string relativeTopPath)
    {
        var packageTop = Path.Combine("Bibliotheque", relativeTopPath);
        var folder = Path.GetDirectoryName(packageTop) ?? "Bibliotheque";
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { packageTop };
        var png = packageTop + ".png";
        if (package.Files.Contains(png, StringComparer.OrdinalIgnoreCase)) result.Add(png);

        if (ComponentNameParser.TryParse(Path.GetFileNameWithoutExtension(relativeTopPath), out var parsed) && parsed is not null)
        {
            foreach (var candidate in package.Files.Where(file =>
                         (Path.GetDirectoryName(file) ?? string.Empty).Equals(folder, StringComparison.OrdinalIgnoreCase) &&
                         Path.GetExtension(file).Equals(".cat", StringComparison.OrdinalIgnoreCase)))
            {
                if (ComponentNameParser.TryParse(Path.GetFileNameWithoutExtension(candidate), out var cat) && cat?.CatKey == parsed.CatKey)
                    result.Add(candidate);
            }
        }
        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<ProductPackageTrashResult> MoveToTrashAsync(
        ExistingProductPackage package,
        string stagingRoot,
        string trashRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDirectChild(stagingRoot, package.FullPath);
        var currentFingerprint = ComputeCurrentFingerprint(package);
        if (!currentFingerprint.Equals(package.Fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Le package a changé depuis son affichage. Recharge la liste avant de le mettre en corbeille.");

        Directory.CreateDirectory(trashRoot);
        var destination = Path.Combine(trashRoot, $"{DateTime.Now:yyyyMMdd_HHmmss}_{package.Name}");
        var fullPackage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.FullPath));
        var fullDestination = Path.GetFullPath(destination);
        if (fullDestination.StartsWith(fullPackage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La corbeille configurée ne peut pas se trouver à l’intérieur du package à déplacer.");
        if (Directory.Exists(destination) || File.Exists(destination))
            destination += "_" + Guid.NewGuid().ToString("N")[..6];
        Directory.Move(package.FullPath, destination);
        await Task.CompletedTask;
        return new ProductPackageTrashResult(package.Name, package.FullPath, destination, package.ComponentCount, package.FileCount, DateTimeOffset.Now);
    }

    public static void EnsureDirectChild(string expectedRoot, string candidate)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedRoot));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (!(Path.GetDirectoryName(full)?.Equals(root, StringComparison.OrdinalIgnoreCase) ?? false))
            throw new InvalidOperationException("L’opération a été refusée : le package n’est pas un enfant direct du dossier configuré.");
    }

    private static bool IsUnderLibrary(string relative) =>
        relative.StartsWith("Bibliotheque" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> EnumeratePackageFiles(string packageRoot, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(packageRoot));
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.AddRange(Directory.EnumerateFiles(directory).Where(file =>
                !Path.GetFileName(file).Equals("biblideo.edit.lock", StringComparison.OrdinalIgnoreCase)));
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
        }
        return files;
    }

    private static string ComputeFingerprint(string packageRoot, IReadOnlyList<string> relativeFiles)
    {
        var builder = new StringBuilder();
        foreach (var relative in relativeFiles)
        {
            var info = new FileInfo(Path.Combine(packageRoot, relative));
            builder.Append(relative.ToUpperInvariant()).Append('|').Append(info.Length).Append('|')
                .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
