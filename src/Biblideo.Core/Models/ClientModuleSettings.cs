namespace Biblideo.Core.Models;

public sealed class ClientModuleSettings
{
    public string ArchivePath { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public string HistoryPath { get; set; } = string.Empty;
    public string SubscriptionMarker { get; set; } = "_";
    public string BiblideoFolderName { get; set; } = "00_Biblideo";
    public List<string> FolderStructure { get; set; } =
    [
        "00_Biblideo",
        "01_Licence_Biblideo",
        "10_Etude",
        "90_Dev_Spe",
        "99_Partage_Client"
    ];
    public List<string> IgnoredFolderNames { get; set; } = [];
    public List<string> NotificationRecipients { get; set; } = [];
}

public sealed record ClientModuleContext(
    string ClientsRootPath,
    string ClientArchivePath,
    string ClientRegistryPath,
    string ClientHistoryPath,
    string ClientSubscriptionMarker,
    string ClientBiblideoFolderName,
    IReadOnlyList<string> ClientFolderStructure,
    IReadOnlyList<string> ClientIgnoredFolderNames,
    IReadOnlyList<string> ClientNotificationRecipients)
{
    public static ClientModuleContext Create(AppSettings appSettings, ClientModuleSettings moduleSettings) => new(
        appSettings.ClientsRootPath,
        moduleSettings.ArchivePath,
        moduleSettings.RegistryPath,
        moduleSettings.HistoryPath,
        moduleSettings.SubscriptionMarker,
        moduleSettings.BiblideoFolderName,
        moduleSettings.FolderStructure,
        moduleSettings.IgnoredFolderNames,
        moduleSettings.NotificationRecipients);
}
