using System.IO;
using System.Text.Json;

namespace Biblideo.App.Services;

public sealed class UiPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public UiPreferencesService(string? localDataRoot = null)
    {
        var root = localDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ideo Solutions",
            "Biblideo");
        _filePath = Path.Combine(root, "ui-preferences.json");
    }

    public UiPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new UiPreferences();
            return JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(_filePath), JsonOptions)
                   ?? new UiPreferences();
        }
        catch
        {
            return new UiPreferences();
        }
    }

    public void Save(UiPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        catch
        {
            // Une préférence visuelle ne doit jamais bloquer le travail métier.
        }
    }
}

public sealed class UiPreferences
{
    public bool ClientsUseCardView { get; set; }
    public bool LicenseTrackingUseCardView { get; set; }
}
