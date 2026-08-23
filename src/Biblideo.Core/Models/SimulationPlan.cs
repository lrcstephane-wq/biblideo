namespace Biblideo.Core.Models;

public sealed record PlannedFile(string SourcePath, string RelativeDestination, string Reason);
public sealed record PlannedDirectory(string RelativeDestination, string Reason);

public sealed class SimulationPlan
{
    public required LibraryVariant Variant { get; init; }
    public required string OperationName { get; init; }
    public required string SourceRoot { get; init; }
    public required string TargetRoot { get; init; }
    public IReadOnlyList<PlannedFile> Files { get; init; } = [];
    public IReadOnlyList<PlannedDirectory> Directories { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> BlockingErrors { get; init; } = [];
    public long TotalBytes { get; init; }
}
