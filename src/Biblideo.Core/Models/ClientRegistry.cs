namespace Biblideo.Core.Models;

public sealed class ClientRegistry
{
    public int SchemaVersion { get; set; } = 1;
    public List<ClientMetadata> Clients { get; set; } = [];
}

public sealed class ClientMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CanonicalName { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public List<string> Emails { get; set; } = [];
    public bool IsArchived { get; set; }
    public string? ArchivedPath { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ClientHistoryEntry
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string MachineName { get; init; } = Environment.MachineName;
    public string UserName { get; init; } = Environment.UserName;
    public required string Action { get; init; }
    public required string ClientName { get; init; }
    public string? Before { get; init; }
    public string? After { get; init; }
    public string? Details { get; init; }
}

public sealed record ClientSubscriptionChange(ClientRecord Client, bool NewStatus);

public sealed record ClientDraft(
    string BaseName,
    ClientLibraryVariant Variant,
    bool IsSubscribed,
    IReadOnlyList<string> Emails);
