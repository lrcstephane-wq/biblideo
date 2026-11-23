using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed partial class LicenseManagementService
{
    public static DateTime? ParseExpiryDate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var match = ExpiryDateRegex().Match(body);
        if (!match.Success) return null;
        return DateTime.TryParseExact(
            match.Groups["date"].Value,
            "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? parsed.Date
            : null;
    }

    public LicenseInboxItem Match(ParsedLicenseMessage message, IReadOnlyList<ClientRecord> clients)
    {
        if (!string.IsNullOrWhiteSpace(message.Error))
            return new LicenseInboxItem { Message = message, MatchStatus = LicenseMatchStatus.Unreadable };

        var recipients = message.Recipients.Select(NormalizeEmail).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = clients
            .Where(client => client.Emails.Select(NormalizeEmail).Any(email => recipients.Contains(email, StringComparer.OrdinalIgnoreCase)))
            .DistinctBy(client => client.MetadataId)
            .ToArray();
        if (candidates.Length == 1)
        {
            var address = candidates[0].Emails.Select(NormalizeEmail).First(email => recipients.Contains(email, StringComparer.OrdinalIgnoreCase));
            return new LicenseInboxItem { Message = message, MatchStatus = LicenseMatchStatus.Matched, MatchedClient = candidates[0], CandidateClients = candidates, MatchedAddress = address };
        }
        return new LicenseInboxItem
        {
            Message = message,
            MatchStatus = candidates.Length == 0 ? LicenseMatchStatus.Unmatched : LicenseMatchStatus.Ambiguous,
            CandidateClients = candidates
        };
    }

    public static IReadOnlyList<ArchivedBiblideoLicense> ParseLicenses(string? body) =>
        ReadLicenseFields(body).Select(field =>
        {
            var id = KeyFingerprint(field.Key);
            var label = field.Key.Length >= 9 ? $"{field.Key[..3]}…{field.Key[^4..]}" : $"Réf. {id[..8]}";
            return new ArchivedBiblideoLicense(id, label, field.ExpiryDate);
        }).ToArray();

    public static string? FindLicenseKey(string? body, string keyId) =>
        ReadLicenseFields(body).FirstOrDefault(field => KeyFingerprint(field.Key) == keyId).Key;

    private static string KeyFingerprint(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static IEnumerable<(string Key, DateTime ExpiryDate)> ReadLicenseFields(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) yield break;
        string? key = null;
        foreach (Match field in LicenseFieldRegex().Matches(body))
        {
            if (field.Groups["key"].Success)
            {
                key = field.Groups["key"].Length > 0 ? field.Groups["key"].Value : null;
                continue;
            }

            if (key is not null && DateTime.TryParseExact(field.Groups["date"].Value, "dd/MM/yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
                yield return (key, expiry);

            key = null;
        }
    }

    public ClientLicenseSummary Summarize(ClientRecord client, IReadOnlyList<ParsedLicenseMessage> messages, DateTime today,
        int warningDays, IEnumerable<string>? unusedKeys = null)
    {
        var unused = unusedKeys?.ToHashSet(StringComparer.Ordinal);
        var licenses = messages.Where(message => string.IsNullOrWhiteSpace(message.Error))
            .SelectMany(message => message.Licenses.Select(license =>
                new BiblideoLicenseDetail(license, message.FullPath, unused?.Contains(license.KeyId) == true)))
            .GroupBy(detail => detail.License.KeyId, StringComparer.Ordinal)
            .Select(group => group.MaxBy(detail => detail.License.ExpiryDate)!)
            .OrderBy(detail => detail.IsUnused)
            .ThenBy(detail => detail.License.ExpiryDate)
            .ThenBy(detail => detail.License.KeyId, StringComparer.Ordinal)
            .ToArray();
        var active = licenses.Where(detail => !detail.IsUnused).ToArray();
        var currentDate = today.Date;
        var warningDate = currentDate.AddDays(Math.Clamp(warningDays, 0, (DateTime.MaxValue.Date - currentDate).Days));
        var expiry = active.FirstOrDefault()?.License.ExpiryDate.Date;
        var unreadable = messages.Count(message => !string.IsNullOrWhiteSpace(message.Error) || message.Licenses.Count == 0);
        int? days = expiry.HasValue ? (expiry.Value - currentDate).Days : null;
        var state = days switch
        {
            < 0 => ClientLicenseState.Expired,
            int remaining when remaining <= warningDays => ClientLicenseState.RenewSoon,
            int => ClientLicenseState.Valid,
            _ when unreadable > 0 => ClientLicenseState.Unreadable,
            _ when licenses.Length > 0 => ClientLicenseState.Unused,
            _ when !client.IsSubscribed => ClientLicenseState.OffSubscription,
            _ => ClientLicenseState.Missing
        };
        return new ClientLicenseSummary
        {
            Client = client,
            ExpiryDate = expiry,
            MessageCount = messages.Count,
            ValidLicenseCount = active.Count(detail => detail.License.ExpiryDate.Date >= currentDate),
            Licenses = licenses,
            HasUpcomingRenewal = warningDays >= 0 && active.Any(detail => detail.License.ExpiryDate.Date >= currentDate
                && detail.License.ExpiryDate.Date <= warningDate),
            UnreadableCount = unreadable,
            State = state,
            DaysRemaining = days
        };
    }

    public LicenseMoveResult MoveVerified(string sourcePath, string clientFolder)
    {
        try
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Le mail source est introuvable.", sourcePath);
            Directory.CreateDirectory(clientFolder);
            var destination = Path.Combine(clientFolder, Path.GetFileName(sourcePath));
            if (File.Exists(destination))
            {
                if (FilesAreIdentical(sourcePath, destination))
                {
                    File.Delete(sourcePath);
                    return new LicenseMoveResult(sourcePath, destination, true, null);
                }
                destination = BuildUniqueDestination(clientFolder, Path.GetFileNameWithoutExtension(sourcePath), Path.GetExtension(sourcePath));
            }
            File.Copy(sourcePath, destination, false);
            if (!FilesAreIdentical(sourcePath, destination))
            {
                try { File.Delete(destination); } catch { }
                throw new IOException("La copie créée ne correspond pas au mail source. Le source a été conservé.");
            }
            File.Delete(sourcePath);
            return new LicenseMoveResult(sourcePath, destination, true, null);
        }
        catch (Exception exception)
        {
            return new LicenseMoveResult(sourcePath, null, false, exception.Message);
        }
    }

    public static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().Trim('<', '>').ToLowerInvariant();

    private static string BuildUniqueDestination(string folder, string baseName, string extension)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(folder, $"{baseName}__{stamp}{extension}");
        return File.Exists(candidate)
            ? Path.Combine(folder, $"{baseName}__{stamp}_{Guid.NewGuid():N}" + extension)
            : candidate;
    }

    private static bool FilesAreIdentical(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length) return false;
        using var sha = SHA256.Create();
        using var firstStream = File.OpenRead(first);
        var firstHash = sha.ComputeHash(firstStream);
        using var secondStream = File.OpenRead(second);
        var secondHash = sha.ComputeHash(secondStream);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }

    [GeneratedRegex(@"\bdate\s*=\s*(?<date>\d{2}/\d{2}/\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExpiryDateRegex();

    [GeneratedRegex(@"\bkey[^\S\r\n]*=[^\S\r\n]*[""']?(?<key>[^\s,;<>""']*)|\bdate[^\S\r\n]*=[^\S\r\n]*[""']?(?:(?<date>\d{2}/\d{2}/\d{4})\b|[^\s,;<>""']*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LicenseFieldRegex();
}
