using System.Text.RegularExpressions;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public static partial class NamingService
{
    [GeneratedRegex(@"^(?<counter>\d{3})_(?<label>.+)_\((?<variant>SC|EP)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductPackageRegex();

    [GeneratedRegex(@"^(?<period>\d{4})_(?<variant>SC|EP)$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthlyPackageRegex();

    public static string BuildProductPackageName(int counter, string label, LibraryVariant variant)
    {
        if (counter is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(counter));
        }

        var cleanLabel = SanitizeLabel(label);
        if (cleanLabel.Length == 0)
        {
            throw new ArgumentException("Le libellé du package est obligatoire.", nameof(label));
        }

        return $"{counter:000}_{cleanLabel}_({variant})";
    }

    public static int FindNextCounter(string archivesRoot, string stagingRoot)
    {
        var max = 0;
        foreach (var root in new[] { archivesRoot, stagingRoot }.Where(Directory.Exists))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var match = ProductPackageRegex().Match(Path.GetFileName(directory));
                if (match.Success && int.TryParse(match.Groups["counter"].Value, out var counter))
                {
                    max = Math.Max(max, counter);
                }
            }
        }
        return Math.Min(999, max + 1);
    }

    public static bool TryParseProductPackage(string folderName, out int counter, out string label, out LibraryVariant variant)
    {
        counter = 0;
        label = string.Empty;
        variant = LibraryVariant.SC;
        var match = ProductPackageRegex().Match(folderName);
        return match.Success &&
               int.TryParse(match.Groups["counter"].Value, out counter) &&
               Enum.TryParse(match.Groups["variant"].Value, true, out variant) &&
               (label = match.Groups["label"].Value).Length > 0;
    }

    public static bool IsValidMonthlyName(string folderName, LibraryVariant variant) =>
        MonthlyPackageRegex().Match(folderName) is { Success: true } match &&
        match.Groups["variant"].Value.Equals(variant.ToString(), StringComparison.OrdinalIgnoreCase);

    public static string BuildExecutableName(string productPackageName)
    {
        if (!TryParseProductPackage(productPackageName, out _, out var label, out _))
        {
            throw new ArgumentException("Nom de package produit invalide.", nameof(productPackageName));
        }
        return label + ".exe";
    }

    private static string SanitizeLabel(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(character =>
            invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }
        return cleaned.Trim('_');
    }
}
