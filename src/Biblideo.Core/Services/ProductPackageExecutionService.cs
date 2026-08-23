using System.Security.Cryptography;
using System.Text.Json;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

/// <summary>
/// Executes the already validated plan with deliberately limited capabilities:
/// new directories and new file copies only. No overwrite, delete, rename or move exists here.
/// </summary>
public sealed class ProductPackageExecutionService
{
    private const string ManifestFileName = "biblideo.manifest.json";

    public async Task<ProductPackageExecutionResult> ExecuteAsync(
        SimulationPlan plan,
        IProgress<PackageCopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (plan.BlockingErrors.Count > 0)
            throw new InvalidOperationException("Le plan contient des erreurs bloquantes et ne peut pas être copié.");

        var targetRoot = Path.GetFullPath(plan.TargetRoot);
        if (Directory.Exists(targetRoot) || File.Exists(targetRoot))
            throw new IOException($"La destination existe déjà. Aucun fichier n’a été écrasé : {targetRoot}");

        var missingSources = plan.Files.Where(file => !File.Exists(file.SourcePath)).Select(file => file.SourcePath).ToArray();
        if (missingSources.Length > 0)
            throw new FileNotFoundException($"{missingSources.Length} fichier(s) source ont disparu depuis la simulation. Relance l’analyse.", missingSources[0]);

        var destinations = plan.Files.Select(file => GetSafeDestination(targetRoot, file.RelativeDestination)).ToArray();
        if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Length)
            throw new InvalidOperationException("Le plan contient plusieurs fichiers vers la même destination.");

        var startedAt = DateTimeOffset.Now;
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in plan.Directories)
            Directory.CreateDirectory(GetSafeDestination(targetRoot, directory.RelativeDestination));

        var copied = new List<CopiedFileResult>(plan.Files.Count);
        for (var index = 0; index < plan.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planned = plan.Files[index];
            var destination = destinations[index];
            var parent = Path.GetDirectoryName(destination)
                         ?? throw new InvalidOperationException($"Destination invalide : {destination}");
            Directory.CreateDirectory(parent);

            await using (var source = new FileStream(planned.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                await source.CopyToAsync(output, 131072, cancellationToken);

            var sourceInfo = new FileInfo(planned.SourcePath);
            var destinationInfo = new FileInfo(destination);
            if (sourceInfo.Length != destinationInfo.Length)
                throw new IOException($"La taille du fichier copié ne correspond pas à la source : {planned.RelativeDestination}");

            var sourceHash = await ComputeSha256Async(planned.SourcePath, cancellationToken);
            var destinationHash = await ComputeSha256Async(destination, cancellationToken);
            if (!sourceHash.Equals(destinationHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Le contrôle SHA-256 a échoué après la copie : {planned.RelativeDestination}");

            copied.Add(new CopiedFileResult(planned.SourcePath, destination, planned.RelativeDestination, destinationInfo.Length, destinationHash));
            progress?.Report(new PackageCopyProgress(index + 1, plan.Files.Count, planned.RelativeDestination));
        }

        var completedAt = DateTimeOffset.Now;
        var packageName = Path.GetFileName(targetRoot);
        var manifestPath = Path.Combine(targetRoot, ManifestFileName);
        var manifest = new
        {
            schemaVersion = 1,
            packageName,
            variant = plan.Variant.ToString(),
            sourceRoot = plan.SourceRoot,
            targetRoot,
            startedAt,
            completedAt,
            copyPolicy = "create-only; no overwrite; no delete; no move",
            files = copied.Select(file => new
            {
                source = file.SourcePath,
                destination = file.RelativeDestination,
                file.Bytes,
                sha256 = file.Sha256
            })
        };
        await using (var manifestStream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, true))
            await JsonSerializer.SerializeAsync(manifestStream, manifest, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);

        return new ProductPackageExecutionResult
        {
            PackageName = packageName,
            TargetRoot = targetRoot,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ManifestPath = manifestPath,
            Files = copied
        };
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException($"Une destination du plan est absolue : {relativePath}");
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var safePrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Une destination sort du dossier du package : {relativePath}");
        return fullPath;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
