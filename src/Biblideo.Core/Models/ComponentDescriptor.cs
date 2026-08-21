namespace Biblideo.Core.Models;

public sealed class ComponentDescriptor
{
    public required string TopFilePath { get; init; }
    public required string RelativeTopPath { get; init; }
    public required string LibraryName { get; init; }
    public required string FamilyName { get; init; }
    public required string TypeName { get; init; }
    public required string ComponentName { get; init; }
    public required string TechnicalName { get; init; }
    public bool IsClassified { get; init; }
    public IReadOnlyList<string> AssociatedFiles { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
