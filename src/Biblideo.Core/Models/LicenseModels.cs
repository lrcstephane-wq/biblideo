namespace Biblideo.Core.Models;

public enum LicenseMatchStatus
{
    Matched,
    Unmatched,
    Ambiguous,
    Unreadable
}

public enum ClientLicenseState
{
    Valid,
    RenewSoon,
    Expired,
    Missing,
    OffSubscription,
    Unreadable,
    Unused
}

public sealed class ParsedLicenseMessage
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public string Subject { get; init; } = string.Empty;
    public IReadOnlyList<string> Recipients { get; init; } = [];
    public DateTime? ExpiryDate { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<ArchivedBiblideoLicense> Licenses { get; init; } = [];
}

public sealed record ArchivedBiblideoLicense(string KeyId, string KeyLabel, DateTime ExpiryDate);

public sealed record BiblideoLicenseDetail(ArchivedBiblideoLicense License, string SourcePath, bool IsUnused);

public sealed class LicenseInboxItem
{
    public required ParsedLicenseMessage Message { get; init; }
    public required LicenseMatchStatus MatchStatus { get; init; }
    public ClientRecord? MatchedClient { get; init; }
    public IReadOnlyList<ClientRecord> CandidateClients { get; init; } = [];
    public string? MatchedAddress { get; init; }
}

public sealed class ClientLicenseSummary
{
    public required ClientRecord Client { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public int MessageCount { get; init; }
    public int ValidLicenseCount { get; init; }
    public IReadOnlyList<BiblideoLicenseDetail> Licenses { get; init; } = [];
    public bool HasUpcomingRenewal { get; init; }
    public bool NeedsAttention => UnreadableCount > 0 || State is ClientLicenseState.Expired or ClientLicenseState.Missing or ClientLicenseState.Unreadable;
    public int UnreadableCount { get; init; }
    public required ClientLicenseState State { get; init; }
    public int? DaysRemaining { get; init; }
}

public sealed class LicenseIndex
{
    public int SchemaVersion { get; set; } = 1;
    public List<LicenseIndexEntry> Entries { get; set; } = [];
}

public sealed class LicenseIndexEntry
{
    public string FullPath { get; set; } = string.Empty;
    public long Length { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public string Subject { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = [];
    public DateTime? ExpiryDate { get; set; }
    public string? Error { get; set; }
    public List<ArchivedBiblideoLicense>? Licenses { get; set; }
}

public sealed class LicenseHistoryEntry
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string MachineName { get; init; } = Environment.MachineName;
    public string UserName { get; init; } = Environment.UserName;
    public required string Action { get; init; }
    public required string SourceFile { get; init; }
    public string? ClientName { get; init; }
    public string? MatchedAddress { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public string? DestinationFile { get; init; }
    public bool Success { get; init; }
    public string? Details { get; init; }
}

public sealed record LicenseMoveResult(string SourcePath, string? DestinationPath, bool Success, string? Error);
