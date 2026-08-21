using System.Text.Json;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsFilePath { get; }

    public SettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ideo Solutions",
            "Biblideo",
            "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(SettingsFilePath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
            ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath)
            ?? throw new InvalidOperationException("Le chemin du fichier de paramètres est invalide.");
        Directory.CreateDirectory(directory);
        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }
}
