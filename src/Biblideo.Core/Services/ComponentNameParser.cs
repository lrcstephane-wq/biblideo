namespace Biblideo.Core.Services;

public sealed record ParsedComponentName(string Type, string Variant, string Index)
{
    public string CatKey => $"{Type}\u001f{Variant}\u001f{Index}";
}

public static class ComponentNameParser
{
    public static bool TryParse(string fileNameWithoutExtension, out ParsedComponentName? parsed)
    {
        parsed = null;
        var firstHash = fileNameWithoutExtension.IndexOf('#');
        var variantMarker = fileNameWithoutExtension.IndexOf("#V=", StringComparison.OrdinalIgnoreCase);
        var indexMarker = fileNameWithoutExtension.IndexOf("#I=", StringComparison.OrdinalIgnoreCase);

        if (firstHash <= 0 || variantMarker < 0 || indexMarker <= variantMarker + 3)
        {
            return false;
        }

        var type = fileNameWithoutExtension[..firstHash].Trim();
        var variant = fileNameWithoutExtension[(variantMarker + 3)..indexMarker].Trim();
        var indexStart = indexMarker + 3;
        var indexEnd = fileNameWithoutExtension.IndexOf('#', indexStart);
        if (indexEnd < 0)
        {
            indexEnd = fileNameWithoutExtension.Length;
        }

        var index = fileNameWithoutExtension[indexStart..indexEnd].Trim();
        if (type.Length == 0 || variant.Length == 0 || index.Length == 0)
        {
            return false;
        }

        parsed = new ParsedComponentName(type, variant, index);
        return true;
    }
}
