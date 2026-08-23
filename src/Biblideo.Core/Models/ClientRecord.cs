namespace Biblideo.Core.Models;

public sealed class ClientRecord
{
    public Guid MetadataId { get; init; }
    public required string BaseName { get; init; }
    public required string DisplayName { get; init; }
    public required string FolderName { get; init; }
    public required string FullPath { get; init; }
    public required ClientLibraryVariant Variant { get; init; }
    public bool IsSubscribed { get; init; }
    public IReadOnlyList<string> Emails { get; init; } = [];
    public IReadOnlyList<string> MissingFolders { get; init; } = [];
    public string? LatestScPackage { get; init; }
    public string? LatestEpPackage { get; init; }
    public string? LatestLegacyPackage { get; init; }

    public bool StructureIsValid => MissingFolders.Count == 0;

    public string LatestDeploymentDisplay => Variant switch
    {
        ClientLibraryVariant.SC => LatestScPackage ?? LatestLegacyPackage ?? "Aucun pack détecté",
        ClientLibraryVariant.EP => LatestEpPackage ?? LatestLegacyPackage ?? "Aucun pack détecté",
        ClientLibraryVariant.SC_EP =>
            $"SC : {LatestScPackage ?? LatestLegacyPackage ?? "—"}  ·  EP : {LatestEpPackage ?? LatestLegacyPackage ?? "—"}",
        _ => "—"
    };
}

public sealed class ClientScanResult
{
    public IReadOnlyList<ClientRecord> Clients { get; init; } = [];
    public IReadOnlyList<string> IgnoredFolders { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
