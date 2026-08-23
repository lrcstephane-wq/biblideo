namespace Biblideo.Core.Models;

public sealed record PackageCopyProgress(int CompletedFiles, int TotalFiles, string RelativeDestination)
{
    public int Percentage => TotalFiles == 0 ? 100 : (int)Math.Clamp(CompletedFiles * 100d / TotalFiles, 0, 100);
}

public sealed record CopiedFileResult(
    string SourcePath,
    string DestinationPath,
    string RelativeDestination,
    long Bytes,
    string Sha256);

public sealed class ProductPackageExecutionResult
{
    public required string PackageName { get; init; }
    public required string TargetRoot { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required string ManifestPath { get; init; }
    public IReadOnlyList<CopiedFileResult> Files { get; init; } = [];
    public long TotalBytes => Files.Sum(file => file.Bytes);
}
