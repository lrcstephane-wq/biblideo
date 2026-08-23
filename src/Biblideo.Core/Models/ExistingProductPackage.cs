namespace Biblideo.Core.Models;

public sealed class ExistingProductPackage
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required LibraryVariant Variant { get; init; }
    public required int Counter { get; init; }
    public required string Label { get; init; }
    public required DateTimeOffset LastModifiedAt { get; init; }
    public required string Fingerprint { get; init; }
    public IReadOnlyList<string> Files { get; init; } = [];
    public IReadOnlyList<string> ComponentRelativeTopPaths { get; init; } = [];
    public bool HasManifest { get; init; }
    public int ComponentCount => ComponentRelativeTopPaths.Count;
    public int FileCount => Files.Count;
    public string VariantLabel => Variant == LibraryVariant.SC ? "SC · Sans chant" : "EP · Entité panneau";
    public string ModifiedLabel => LastModifiedAt.ToString("dd/MM/yyyy HH:mm");
    public string ManifestLabel => HasManifest ? "Manifeste disponible" : "Ancien package · contenu reconstruit";
}

public sealed class ExistingPackageCatalog
{
    public IReadOnlyList<ExistingProductPackage> Packages { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
