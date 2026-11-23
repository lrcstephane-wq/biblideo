using System.Security.Cryptography;
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

    public ClientLicenseSummary Summarize(ClientRecord client, IReadOnlyList<ParsedLicenseMessage> messages, DateTime today, int warningDays)
    {
        var validDates = messages.Where(message => message.ExpiryDate.HasValue).Select(message => message.ExpiryDate!.Value.Date).ToArray();
        var expiry = validDates.Length == 0 ? (DateTime?)null : validDates.Max();
        var currentDate = today.Date;
        var unreadable = messages.Count(message => !string.IsNullOrWhiteSpace(message.Error) || message.ExpiryDate is null);
        var validCount = messages.Count(message =>
            string.IsNullOrWhiteSpace(message.Error) && message.ExpiryDate is { } date && date.Date >= currentDate);
        int? days = expiry.HasValue ? (expiry.Value - currentDate).Days : null;
        var state = (client.IsSubscribed, days) switch
        {
            (false, _) => ClientLicenseState.OffSubscription,
            (_, null) => messages.Count > 0 ? ClientLicenseState.Unreadable : ClientLicenseState.Missing,
            (_, < 0) => ClientLicenseState.Expired,
            (_, int remaining) when remaining <= warningDays => ClientLicenseState.RenewSoon,
            _ => ClientLicenseState.Valid
        };
        return new ClientLicenseSummary
        {
            Client = client,
            ExpiryDate = expiry,
            MessageCount = messages.Count,
            ValidLicenseCount = validCount,
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
}
