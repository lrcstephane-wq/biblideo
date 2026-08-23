using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class LibraryScanner
{
    public Task<LibraryScanResult> ScanAsync(string sourceRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(sourceRoot, cancellationToken), cancellationToken);

    private static LibraryScanResult Scan(string sourceRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new ArgumentException("Le chemin source n’est pas renseigné.", nameof(sourceRoot));
        }

        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"La bibliothèque source est introuvable : {sourceRoot}");
        }

        var warnings = new List<string>();
        var allFiles = EnumerateFilesSafely(sourceRoot, warnings, cancellationToken);
        var catIndex = BuildCatIndex(allFiles);
        var assets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var components = new List<ComponentDescriptor>();
        var ignoredBak = 0;
        var ignoredCfg = 0;
        var extensionless = 0;

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(file);
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var library = segments.Length > 1 ? segments[0] : "Racine";

            if (extension.Equals(".bak", StringComparison.OrdinalIgnoreCase))
            {
                ignoredBak++;
                continue;
            }

            if (extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase))
            {
                ignoredCfg++;
                continue;
            }

            if (string.IsNullOrEmpty(extension))
            {
                extensionless++;
                warnings.Add($"Fichier sans extension ignoré : {relativePath}");
                continue;
            }

            var fileName = Path.GetFileName(file);
            if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("icn", StringComparison.OrdinalIgnoreCase))
            {
                if (!assets.TryGetValue(library, out var libraryAssets))
                {
                    libraryAssets = [];
                    assets[library] = libraryAssets;
                }
                libraryAssets.Add(file);
            }

            if (!extension.Equals(".top", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var technicalName = Path.GetFileNameWithoutExtension(file);
            var classified = ComponentNameParser.TryParse(technicalName, out var parsed);
            var componentWarnings = new List<string>();
            var associated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var png = file + ".png";
            if (File.Exists(png))
            {
                associated.Add(png);
            }

            if (classified && parsed is not null && catIndex.TryGetValue(parsed.CatKey, out var cats))
            {
                foreach (var cat in cats)
                {
                    associated.Add(cat);
                }
            }

            if (!classified)
            {
                componentWarnings.Add("Nom non conforme : composant placé dans « Non classés ».");
            }

            components.Add(new ComponentDescriptor
            {
                TopFilePath = file,
                RelativeTopPath = relativePath,
                LibraryName = library,
                FamilyName = segments.Length > 2 ? segments[1] : "Racine",
                TypeName = parsed?.Type ?? "Non classés",
                ComponentName = parsed?.Variant ?? technicalName,
                TechnicalName = technicalName,
                IsClassified = classified,
                AssociatedFiles = associated.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Warnings = componentWarnings
            });
        }

        return new LibraryScanResult
        {
            SourceRoot = sourceRoot,
            Components = components
                .OrderBy(item => item.LibraryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ComponentName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AutomaticAssetsByLibrary = assets.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase),
            Warnings = warnings,
            IgnoredBakCount = ignoredBak,
            IgnoredCfgCount = ignoredCfg,
            ExtensionlessCount = extensionless
        };
    }

    private static Dictionary<string, List<string>> BuildCatIndex(IEnumerable<string> files)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(path => Path.GetExtension(path).Equals(".cat", StringComparison.OrdinalIgnoreCase)))
        {
            if (!ComponentNameParser.TryParse(Path.GetFileNameWithoutExtension(file), out var parsed) || parsed is null)
            {
                continue;
            }

            if (!index.TryGetValue(parsed.CatKey, out var matches))
            {
                matches = [];
                index[parsed.CatKey] = matches;
            }
            matches.Add(file);
        }
        return index;
    }

    private static IReadOnlyList<string> EnumerateFilesSafely(
        string sourceRoot,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(Path.GetFullPath(sourceRoot));

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(directory)) continue;

            try
            {
                files.AddRange(Directory.EnumerateFiles(directory));
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Dossier partiellement inaccessible : {Path.GetRelativePath(sourceRoot, directory)} ({exception.Message})");
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            warnings.Add($"Lien de dossier ignoré : {Path.GetRelativePath(sourceRoot, child)}");
                            continue;
                        }
                    }
                    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                    {
                        warnings.Add($"Dossier inaccessible ignoré : {Path.GetRelativePath(sourceRoot, child)} ({exception.Message})");
                        continue;
                    }
                    pending.Push(child);
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                warnings.Add($"Sous-dossiers inaccessibles : {Path.GetRelativePath(sourceRoot, directory)} ({exception.Message})");
            }
        }

        return files;
    }
}
