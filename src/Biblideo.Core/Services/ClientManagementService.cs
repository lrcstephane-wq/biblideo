using System.Text.RegularExpressions;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed partial class ClientManagementService
{
    public ClientScanResult Scan(ClientModuleContext settings, ClientRegistry registry)
    {
        if (!Directory.Exists(settings.ClientsRootPath))
            throw new DirectoryNotFoundException($"Racine clients introuvable : {settings.ClientsRootPath}");

        var clients = new List<ClientRecord>();
        var ignored = new List<string>();
        var warnings = new List<string>();
        var metadataByFolder = registry.Clients
            .Where(item => !item.IsArchived && !string.IsNullOrWhiteSpace(item.FolderName))
            .GroupBy(item => item.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var metadataByCanonical = registry.Clients
            .Where(item => !item.IsArchived && !string.IsNullOrWhiteSpace(item.CanonicalName))
            .GroupBy(item => item.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var fullPath in Directory.EnumerateDirectories(settings.ClientsRootPath).OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
        {
            var folderName = Path.GetFileName(fullPath);
            if (settings.ClientIgnoredFolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase)) continue;
            if (!ClientFolderNamingService.TryParse(folderName, settings.ClientSubscriptionMarker, out var parsed) || parsed is null)
            {
                ignored.Add(folderName);
                continue;
            }

            var canonical = ClientFolderNamingService.BuildCanonicalName(parsed.BaseName);
            metadataByFolder.TryGetValue(folderName, out var metadata);
            metadata ??= metadataByCanonical.GetValueOrDefault(canonical);
            var missingFolders = settings.ClientFolderStructure
                .Where(required => !Directory.Exists(Path.Combine(fullPath, required)))
                .ToArray();
            var deployment = ScanLatestDeployment(Path.Combine(fullPath, settings.ClientBiblideoFolderName), parsed.Variant);

            clients.Add(new ClientRecord
            {
                MetadataId = metadata?.Id ?? Guid.NewGuid(),
                BaseName = parsed.BaseName,
                DisplayName = parsed.DisplayName,
                FolderName = folderName,
                FullPath = fullPath,
                Variant = parsed.Variant,
                IsSubscribed = parsed.IsSubscribed,
                Emails = metadata?.Emails ?? [],
                MissingFolders = missingFolders,
                LatestScPackage = deployment.Sc,
                LatestEpPackage = deployment.Ep,
                LatestLegacyPackage = deployment.Legacy
            });
        }

        if (ignored.Count > 0)
            warnings.Add($"{ignored.Count} dossier(s) ne correspondent pas au format client Biblidéo et ont été ignorés.");
        return new ClientScanResult { Clients = clients, IgnoredFolders = ignored, Warnings = warnings };
    }

    public async Task<IReadOnlyList<(ClientSubscriptionChange Change, string OldPath, string NewPath)>> ApplySubscriptionChangesAsync(
        ClientModuleContext settings,
        IReadOnlyList<ClientSubscriptionChange> changes,
        CancellationToken cancellationToken = default)
    {
        var operations = changes
            .Where(change => change.Client.IsSubscribed != change.NewStatus)
            .Select(change =>
            {
                EnsureImmediateChild(settings.ClientsRootPath, change.Client.FullPath);
                var targetName = ClientFolderNamingService.BuildFolderName(
                    change.Client.BaseName,
                    change.Client.Variant,
                    change.NewStatus,
                    settings.ClientSubscriptionMarker);
                return (Change: change, OldPath: change.Client.FullPath, NewPath: Path.Combine(settings.ClientsRootPath, targetName));
            })
            .ToArray();

        var duplicateTargets = operations.GroupBy(item => item.NewPath, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicateTargets is not null)
            throw new IOException($"Plusieurs modifications visent le même dossier : {duplicateTargets.Key}");
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(operation.OldPath)) throw new DirectoryNotFoundException($"Dossier client introuvable : {operation.OldPath}");
            if (!operation.OldPath.Equals(operation.NewPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(operation.NewPath))
                throw new IOException($"Le dossier cible existe déjà : {operation.NewPath}");
        }

        var completed = new List<(ClientSubscriptionChange Change, string OldPath, string NewPath)>();
        try
        {
            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(operation.OldPath, operation.NewPath);
                completed.Add(operation);
                await Task.Yield();
            }
            return completed;
        }
        catch
        {
            foreach (var operation in completed.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(operation.NewPath) && !Directory.Exists(operation.OldPath))
                        Directory.Move(operation.NewPath, operation.OldPath);
                }
                catch
                {
                    // L’analyse suivante signalera explicitement toute situation non restaurée.
                }
            }
            throw;
        }
    }

    public string CreateClient(ClientModuleContext settings, ClientDraft draft)
    {
        var folderName = ClientFolderNamingService.BuildFolderName(
            draft.BaseName,
            draft.Variant,
            draft.IsSubscribed,
            settings.ClientSubscriptionMarker);
        var targetPath = Path.Combine(settings.ClientsRootPath, folderName);
        EnsureImmediateChild(settings.ClientsRootPath, targetPath);
        if (Directory.Exists(targetPath)) throw new IOException($"Le dossier client existe déjà : {targetPath}");
        Directory.CreateDirectory(targetPath);
        foreach (var folder in settings.ClientFolderStructure)
            Directory.CreateDirectory(Path.Combine(targetPath, folder));
        return targetPath;
    }

    public string UpdateClientFolder(ClientModuleContext settings, ClientRecord client, ClientDraft draft)
    {
        EnsureImmediateChild(settings.ClientsRootPath, client.FullPath);
        var targetName = ClientFolderNamingService.BuildFolderName(
            draft.BaseName,
            draft.Variant,
            draft.IsSubscribed,
            settings.ClientSubscriptionMarker);
        var targetPath = Path.Combine(settings.ClientsRootPath, targetName);
        if (!client.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(targetPath)) throw new IOException($"Le dossier cible existe déjà : {targetPath}");
            Directory.Move(client.FullPath, targetPath);
        }
        return targetPath;
    }

    public IReadOnlyList<string> RepairStructure(ClientModuleContext settings, ClientRecord client)
    {
        EnsureImmediateChild(settings.ClientsRootPath, client.FullPath);
        var created = new List<string>();
        foreach (var folder in settings.ClientFolderStructure)
        {
            var path = Path.Combine(client.FullPath, folder);
            if (Directory.Exists(path)) continue;
            Directory.CreateDirectory(path);
            created.Add(path);
        }
        return created;
    }

    public ClientFolderInventory InspectFolder(ClientModuleContext settings, ClientRecord client)
    {
        EnsureImmediateChild(settings.ClientsRootPath, client.FullPath);
        long size = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(client.FullPath, "*", SearchOption.AllDirectories))
        {
            count++;
            try { size += new FileInfo(file).Length; } catch { /* Le nombre reste fiable même si la taille est momentanément inaccessible. */ }
        }
        return new ClientFolderInventory(count, size);
    }

    public string ArchiveClient(ClientModuleContext settings, ClientRecord client)
    {
        EnsureImmediateChild(settings.ClientsRootPath, client.FullPath);
        Directory.CreateDirectory(settings.ClientArchivePath);
        var archiveName = $"{DateTime.Now:yyyyMMdd-HHmmss}__{client.FolderName}";
        var archivePath = Path.Combine(settings.ClientArchivePath, archiveName);
        if (Directory.Exists(archivePath)) archivePath += $"__{Guid.NewGuid():N}"[..8];
        Directory.Move(client.FullPath, archivePath);
        return archivePath;
    }

    public void PermanentlyDeleteEmptyClient(ClientModuleContext settings, ClientRecord client)
    {
        EnsureImmediateChild(settings.ClientsRootPath, client.FullPath);
        if (Directory.EnumerateFiles(client.FullPath, "*", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("Le dossier contient au moins un fichier et ne peut pas être supprimé définitivement.");
        Directory.Delete(client.FullPath, recursive: true);
    }

    public string RestoreArchivedClient(ClientModuleContext settings, ClientMetadata metadata)
    {
        if (!metadata.IsArchived || string.IsNullOrWhiteSpace(metadata.ArchivedPath))
            throw new InvalidOperationException("Cette entrée n’est pas une archive client valide.");
        if (!Directory.Exists(metadata.ArchivedPath))
            throw new DirectoryNotFoundException($"Archive introuvable : {metadata.ArchivedPath}");
        var archiveRoot = Path.GetFullPath(settings.ClientArchivePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var archiveParent = Directory.GetParent(Path.GetFullPath(metadata.ArchivedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
            ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(archiveRoot, archiveParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La restauration a été bloquée car l’archive n’est pas située dans le dossier configuré.");
        var targetPath = Path.Combine(settings.ClientsRootPath, metadata.FolderName);
        EnsureImmediateChild(settings.ClientsRootPath, targetPath);
        if (Directory.Exists(targetPath)) throw new IOException($"Un dossier client porte déjà ce nom : {targetPath}");
        Directory.Move(metadata.ArchivedPath, targetPath);
        return targetPath;
    }

    private static (string? Sc, string? Ep, string? Legacy) ScanLatestDeployment(string biblideoPath, ClientLibraryVariant clientVariant)
    {
        if (!Directory.Exists(biblideoPath)) return (null, null, null);
        string? sc = null;
        string? ep = null;
        string? legacy = null;
        foreach (var path in Directory.EnumerateFileSystemEntries(biblideoPath))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var match = MonthlyPackageRegex().Match(name);
            if (!match.Success) continue;
            var period = match.Groups["period"].Value;
            if (!IsValidPeriod(period)) continue;
            var variant = match.Groups["variant"].Value.ToUpperInvariant();
            if (variant == "SC") sc = MaxPeriod(sc, period);
            else if (variant == "EP") ep = MaxPeriod(ep, period);
            else if (variant is "SC+EP" or "EP+SC" or "SC_EP" or "EP_SC")
            {
                sc = MaxPeriod(sc, period);
                ep = MaxPeriod(ep, period);
            }
            else legacy = MaxPeriod(legacy, period);
        }

        if (legacy is not null)
        {
            if (clientVariant == ClientLibraryVariant.SC) sc ??= legacy;
            if (clientVariant == ClientLibraryVariant.EP) ep ??= legacy;
        }
        return (sc, ep, legacy);
    }

    private static bool IsValidPeriod(string period) =>
        period.Length == 4 && int.TryParse(period[2..], out var month) && month is >= 1 and <= 12;

    private static string MaxPeriod(string? current, string candidate) =>
        current is null || string.CompareOrdinal(candidate, current) > 0 ? candidate : current;

    private static void EnsureImmediateChild(string root, string child)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(fullChild)?.FullName?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(parent, fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L’opération a été bloquée car le dossier n’est pas un client direct de la racine configurée.");
    }

    [GeneratedRegex(@"^(?<period>\d{4})(?:_(?<variant>SC[+_]EP|EP[+_]SC|SC|EP))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthlyPackageRegex();
}

public sealed record ClientFolderInventory(int FileCount, long TotalBytes)
{
    public bool IsEmpty => FileCount == 0;
}
