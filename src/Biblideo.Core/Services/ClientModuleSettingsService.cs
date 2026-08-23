using System.Text.Json;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class ClientModuleSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _machinePathsFilePath;

    public ClientModuleSettingsService(string? localDataRoot = null)
    {
        var root = localDataRoot ?? SettingsDefaults.ApplicationDataRoot;
        _machinePathsFilePath = Path.Combine(root, "client-machine-paths.json");
    }

    public string GetSettingsPath(AppSettings appSettings)
    {
        var directory = Path.GetDirectoryName(appSettings.SharedSettingsPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Le dossier de configuration partagée est invalide.");
        return Path.Combine(directory, "biblideo.clients.settings.json");
    }

    public async Task<ClientModuleSettings> LoadAsync(AppSettings appSettings, CancellationToken cancellationToken = default)
    {
        var path = GetSettingsPath(appSettings);
        ClientModuleSettings settings;
        if (File.Exists(path))
        {
            await using var stream = File.OpenRead(path);
            settings = await JsonSerializer.DeserializeAsync<ClientModuleSettings>(stream, JsonOptions, cancellationToken)
                       ?? new ClientModuleSettings();
        }
        else settings = new ClientModuleSettings();
        ApplyDefaults(appSettings, settings);
        await ApplyMachinePathsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task SaveAsync(AppSettings appSettings, ClientModuleSettings settings, CancellationToken cancellationToken = default)
    {
        ApplyDefaults(appSettings, settings);
        var path = GetSettingsPath(appSettings);
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Le dossier de configuration clients est invalide.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, path, true);
        await WriteMachinePathsAsync(settings, cancellationToken);
    }

    private static void ApplyDefaults(AppSettings appSettings, ClientModuleSettings settings)
    {
        var configDirectory = Path.GetDirectoryName(appSettings.SharedSettingsPath)
                              ?? throw new InvalidOperationException("Le dossier de configuration partagée est invalide.");
        var applicationRoot = Directory.GetParent(configDirectory)?.FullName ?? configDirectory;
        if (string.IsNullOrWhiteSpace(settings.ArchivePath)) settings.ArchivePath = Path.Combine(appSettings.IdeoSolutionsRootPath, "99_Z_Archives");
        if (string.IsNullOrWhiteSpace(settings.RegistryPath)) settings.RegistryPath = Path.Combine(applicationRoot, "Data", "clients.json");
        if (string.IsNullOrWhiteSpace(settings.HistoryPath)) settings.HistoryPath = Path.Combine(applicationRoot, "Data", "clients-history.jsonl");
        if (string.IsNullOrWhiteSpace(settings.SubscriptionMarker)) settings.SubscriptionMarker = "_";
        if (settings.FolderStructure.Count == 0) settings.FolderStructure = new ClientModuleSettings().FolderStructure;
    }

    private async Task ApplyMachinePathsAsync(ClientModuleSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(_machinePathsFilePath)) return;
        await using var stream = File.OpenRead(_machinePathsFilePath);
        var paths = await JsonSerializer.DeserializeAsync<ClientMachinePaths>(stream, JsonOptions, cancellationToken);
        if (paths is null) return;
        settings.ArchivePath = paths.ArchivePath;
        settings.RegistryPath = paths.RegistryPath;
        settings.HistoryPath = paths.HistoryPath;
    }

    private async Task WriteMachinePathsAsync(ClientModuleSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_machinePathsFilePath)
            ?? throw new InvalidOperationException("Le chemin des réglages clients de ce poste est invalide.");
        Directory.CreateDirectory(directory);
        var temp = _machinePathsFilePath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, new ClientMachinePaths
            {
                ArchivePath = settings.ArchivePath,
                RegistryPath = settings.RegistryPath,
                HistoryPath = settings.HistoryPath
            }, JsonOptions, cancellationToken);
        }
        File.Move(temp, _machinePathsFilePath, true);
    }

    private sealed class ClientMachinePaths
    {
        public string ArchivePath { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public string HistoryPath { get; set; } = string.Empty;
    }
}
