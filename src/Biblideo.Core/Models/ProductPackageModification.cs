namespace Biblideo.Core.Models;

public enum ExistingFileCopyMode { Overwrite, Preserve }
public enum PackageFileActionKind { Add, Overwrite, Preserve, Remove, Unmanaged }

public sealed record PackageFileAction(
    PackageFileActionKind Kind,
    string RelativePath,
    string? SourcePath,
    string DestinationPath,
    string Reason);

public sealed class ProductPackageModificationPlan
{
    public required ExistingProductPackage ExistingPackage { get; init; }
    public required ExistingFileCopyMode CopyMode { get; init; }
    public required SimulationPlan DesiredPlan { get; init; }
    public IReadOnlyList<string> AddedComponents { get; init; } = [];
    public IReadOnlyList<string> RemovedComponents { get; init; } = [];
    public IReadOnlyList<string> RetainedComponents { get; init; } = [];
    public IReadOnlyList<string> OrphanComponents { get; init; } = [];
    public IReadOnlyList<PackageFileAction> FileActions { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> BlockingErrors { get; init; } = [];
    public int AddedFileCount => FileActions.Count(action => action.Kind == PackageFileActionKind.Add);
    public int OverwrittenFileCount => FileActions.Count(action => action.Kind == PackageFileActionKind.Overwrite);
    public int PreservedFileCount => FileActions.Count(action => action.Kind == PackageFileActionKind.Preserve);
    public int RemovedFileCount => FileActions.Count(action => action.Kind == PackageFileActionKind.Remove);
}

public sealed record ModifiedFileResult(
    PackageFileActionKind Action,
    string RelativePath,
    string? Sha256,
    long Bytes);

public sealed class ProductPackageModificationResult
{
    public required string PackageName { get; init; }
    public required string PackagePath { get; init; }
    public required string RecoveryPath { get; init; }
    public required string ManifestPath { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public IReadOnlyList<ModifiedFileResult> Files { get; init; } = [];
}

public sealed record ProductPackageTrashResult(
    string PackageName,
    string OriginalPath,
    string TrashPath,
    int ComponentCount,
    int FileCount,
    DateTimeOffset MovedAt);
