using System.Text.Json.Serialization;

namespace Biblideo.Core.Models;

public sealed class AppSettings
{
    [JsonIgnore]
    public string SharedSettingsPath { get; set; } = SettingsDefaults.SharedSettingsPath;

    public string SourceScPath { get; set; } = string.Empty;
    public string SourceEpPath { get; set; } = string.Empty;
    public string ClientsRootPath { get; set; } = string.Empty;
    public string IdeoSolutionsRootPath { get; set; } = string.Empty;
    public string ProductStagingPath { get; set; } = string.Empty;
    public string ProductArchivesPath { get; set; } = string.Empty;
    public string ProductTrashPath { get; set; } = string.Empty;
    public string ReportsPath { get; set; } = string.Empty;
    public string MonthlyPackagesRootPath { get; set; } = string.Empty;
    public string SevenZipExecutablePath { get; set; } = @"C:\Program Files\7-Zip\7z.exe";
    public string LicenseInboxPath { get; set; } = string.Empty;
    public string LicenseIndexPath { get; set; } = string.Empty;
    public string LicenseHistoryPath { get; set; } = string.Empty;
    public string LicenseClientFolderName { get; set; } = "01_Licence_Biblideo";
    public int LicenseRenewalWarningDays { get; set; } = 10;
    public string LicensePlannerUrl { get; set; } = "https://planner.topsolid.com/User/Default.aspx";

    [JsonIgnore]
    public string ScModelPath => Path.Combine(ProductArchivesPath, "000_Dossier_Produits_(SC)");

    [JsonIgnore]
    public string EpModelPath => Path.Combine(ProductArchivesPath, "000_Dossier_Produits_(EP)");

    public string GetSourcePath(LibraryVariant variant) =>
        variant == LibraryVariant.SC ? SourceScPath : SourceEpPath;

    public string GetModelPath(LibraryVariant variant) =>
        variant == LibraryVariant.SC ? ScModelPath : EpModelPath;

    public string GetEffectiveLicenseInboxPath() => string.IsNullOrWhiteSpace(LicenseInboxPath)
        ? CombineWhenConfigured(IdeoSolutionsRootPath, LicenseClientFolderName)
        : LicenseInboxPath;

    public string GetEffectiveLicenseIndexPath() => string.IsNullOrWhiteSpace(LicenseIndexPath)
        ? Path.Combine(GetApplicationDataRoot(), "Data", "licenses-index.json")
        : LicenseIndexPath;

    public string GetEffectiveLicenseHistoryPath() => string.IsNullOrWhiteSpace(LicenseHistoryPath)
        ? Path.Combine(GetApplicationDataRoot(), "Data", "licenses-history.jsonl")
        : LicenseHistoryPath;

    private string GetApplicationDataRoot()
    {
        var configDirectory = Path.GetDirectoryName(SharedSettingsPath);
        return string.IsNullOrWhiteSpace(configDirectory)
            ? SettingsDefaults.ApplicationDataRoot
            : Directory.GetParent(configDirectory)?.FullName ?? configDirectory;
    }

    private static string CombineWhenConfigured(string root, string child) =>
        string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, child);
}

public static class SettingsDefaults
{
    public static string ApplicationDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions",
        "Biblideo");

    public static string SharedSettingsPath => Path.Combine(ApplicationDataRoot, "Config", "biblideo.settings.json");
}
