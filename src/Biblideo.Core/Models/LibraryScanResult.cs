namespace Biblideo.Core.Models;

public sealed class LibraryScanResult
{
    public required string SourceRoot { get; init; }
    public IReadOnlyList<ComponentDescriptor> Components { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AutomaticAssetsByLibrary { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int IgnoredBakCount { get; init; }
    public int IgnoredCfgCount { get; init; }
    public int ExtensionlessCount { get; init; }
}
