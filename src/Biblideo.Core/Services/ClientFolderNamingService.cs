using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public static partial class ClientFolderNamingService
{
    public static bool TryParse(string folderName, string subscriptionMarker, out ParsedClientFolder? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(subscriptionMarker)) return false;

        var marker = Regex.Escape(subscriptionMarker);
        var match = Regex.Match(
            folderName,
            $"^(?<active>{marker})?(?<name>.+)_\\((?<variant>SC|EP|SC\\+EP|EP\\+SC)\\)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        var baseName = match.Groups["name"].Value.Trim();
        if (string.IsNullOrWhiteSpace(baseName)) return false;
        var token = match.Groups["variant"].Value.ToUpperInvariant();
        var variant = token switch
        {
            "SC" => ClientLibraryVariant.SC,
            "EP" => ClientLibraryVariant.EP,
            "SC+EP" => ClientLibraryVariant.SC_EP,
            "EP+SC" => ClientLibraryVariant.SC_EP,
            _ => throw new InvalidOperationException("Version client inconnue.")
        };

        parsed = new ParsedClientFolder(
            baseName,
            baseName.Replace('_', ' '),
            variant,
            match.Groups["active"].Success);
        return true;
    }

    public static string BuildFolderName(string baseName, ClientLibraryVariant variant, bool subscribed, string subscriptionMarker)
    {
        var normalized = NormalizeFolderBaseName(baseName);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Le nom du client est obligatoire.");
        if (string.IsNullOrWhiteSpace(subscriptionMarker))
            throw new InvalidOperationException("Le marqueur des mises à jour est obligatoire.");
        if (subscriptionMarker.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Le marqueur des mises à jour contient un caractère interdit par Windows.");

        return $"{(subscribed ? subscriptionMarker : string.Empty)}{normalized}_({variant.ToFolderToken()})";
    }

    public static string NormalizeFolderBaseName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Le nom du client contient un caractère interdit par Windows.");
        return WhitespaceRegex().Replace(trimmed, "_").Trim('_');
    }

    public static string BuildCanonicalName(string value)
    {
        var normalized = NormalizeFolderBaseName(value).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }
        return DuplicateUnderscoreRegex().Replace(builder.ToString(), "_").Trim('_');
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"_+", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateUnderscoreRegex();
}

public sealed record ParsedClientFolder(
    string BaseName,
    string DisplayName,
    ClientLibraryVariant Variant,
    bool IsSubscribed);
