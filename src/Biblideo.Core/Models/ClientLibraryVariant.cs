namespace Biblideo.Core.Models;

public enum ClientLibraryVariant
{
    SC,
    EP,
    SC_EP
}

public static class ClientLibraryVariantExtensions
{
    public static string ToFolderToken(this ClientLibraryVariant variant) => variant switch
    {
        ClientLibraryVariant.SC => "SC",
        ClientLibraryVariant.EP => "EP",
        ClientLibraryVariant.SC_EP => "SC+EP",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
    };

    public static string ToDisplayName(this ClientLibraryVariant variant) => variant switch
    {
        ClientLibraryVariant.SC => "SC · Sans chant",
        ClientLibraryVariant.EP => "EP · Entité panneau",
        ClientLibraryVariant.SC_EP => "SC + EP",
        _ => variant.ToString()
    };
}
