using System.IO;
using System.Text.Json;

namespace Biblideo.App.Services;

internal sealed record NexusRegistryLocation(string NasRoot, string CloudRoot, bool UseCloud)
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions", "Nexus", "registre-emplacement.json");

    public string? ActiveRoot => (UseCloud ? CloudRoot : NasRoot) is { Length: > 0 } path ? path : null;

    public static NexusRegistryLocation Load()
    {
        if (NexusPreviewProfile.Enabled || !File.Exists(SettingsPath)) return new("", "", false);
        return JsonSerializer.Deserialize<NexusRegistryLocation>(File.ReadAllText(SettingsPath))
            ?? throw new InvalidDataException("Paramètres de registre Nexus illisibles.");
    }

    public void Save()
    {
        if (NexusPreviewProfile.Enabled) return;
        if (ActiveRoot is null) throw new ArgumentException("Renseigne la racine utilisée pour le registre.");
        if (NasRoot.Length != 0 && (!Path.IsPathFullyQualified(NasRoot) || !IsNetworkPath(NasRoot)))
            throw new ArgumentException("Choisis un partage NAS (chemin UNC ou lecteur réseau).");
        if (CloudRoot.Length != 0 && !Path.IsPathFullyQualified(CloudRoot))
            throw new ArgumentException("Choisis le dossier cloud dans l’Explorateur.");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temp = SettingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static bool IsNetworkPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network;
}
