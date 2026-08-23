using System.Text.Json.Serialization;

namespace Biblideo.Core.Models;

public sealed class AppSettings
{
    [JsonIgnore]
    public string SharedSettingsPath { get; set; } = SettingsDefaults.SharedSettingsPath;

    public string SourceScPath { get; set; } = @"C:\Cloud\95_TSV6\Missler_622\BT_Group622\Bibliotheque";
    public string SourceEpPath { get; set; } = @"C:\Cloud\95_TSV6\Missler_622\EP_Biblideo_Chant_Grp622\Bibliotheque";
    public string ClientsRootPath { get; set; } = @"C:\Cloud\20_Partage_Clients\Biblidéo";
    public string IdeoSolutionsRootPath { get; set; } = @"C:\Cloud\20_Partage_Clients\Biblidéo\Idéo_Solutions";
    public string ProductStagingPath { get; set; } = @"C:\Cloud\20_Partage_Clients\Biblidéo\Idéo_Solutions\000_Composants_A_Deployers";
    public string ProductArchivesPath { get; set; } = @"C:\Cloud\20_Partage_Clients\Biblidéo\Idéo_Solutions\000_Composants_Deja_Deployes_ARCHIVES";
    public string ProductTrashPath { get; set; } = @"C:\Cloud\20_Partage_Clients\Biblidéo\Idéo_Solutions\000_Corbeille_Packages";
    public string ReportsPath { get; set; } = @"C:\Cloud\20_Partage_Clients\Biblidéo\Idéo_Solutions\000_Rapports";
    public string MonthlyPackagesRootPath { get; set; } = @"C:\Cloud\20_Partage_Clients\Biblidéo\Idéo_Solutions";
    public string SevenZipExecutablePath { get; set; } = @"C:\Program Files\7-Zip\7z.exe";

    [JsonIgnore]
    public string ScModelPath => Path.Combine(ProductArchivesPath, "000_Dossier_Produits_(SC)");

    [JsonIgnore]
    public string EpModelPath => Path.Combine(ProductArchivesPath, "000_Dossier_Produits_(EP)");

    public string GetSourcePath(LibraryVariant variant) =>
        variant == LibraryVariant.SC ? SourceScPath : SourceEpPath;

    public string GetModelPath(LibraryVariant variant) =>
        variant == LibraryVariant.SC ? ScModelPath : EpModelPath;
}

public static class SettingsDefaults
{
    public const string ApplicationDataRoot = @"C:\Cloud\02_Idéo\99_Application\Biblideo";
    public const string SharedSettingsPath = ApplicationDataRoot + @"\Config\biblideo.settings.json";
}
