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

    public string BootstrapFilePath { get; }
    public string CacheFilePath { get; }
    public bool LastLoadUsedCache { get; private set; }
    public string ActiveSharedSettingsPath { get; private set; } = SettingsDefaults.SharedSettingsPath;

    public SettingsService(string? localDataRoot = null)
    {
        var root = localDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ideo Solutions",
            "Biblideo");
        BootstrapFilePath = Path.Combine(root, "bootstrap.json");
        CacheFilePath = Path.Combine(root, "shared-settings.cache.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        ActiveSharedSettingsPath = await LoadSharedPathAsync(cancellationToken);
        LastLoadUsedCache = false;

        if (File.Exists(ActiveSharedSettingsPath))
        {
            var shared = await ReadSettingsAsync(ActiveSharedSettingsPath, cancellationToken);
            shared.SharedSettingsPath = ActiveSharedSettingsPath;
            await WriteSettingsAtomicallyAsync(CacheFilePath, shared, cancellationToken, createBackup: false);
            return shared;
        }

        if (File.Exists(CacheFilePath))
        {
            LastLoadUsedCache = true;
            var cached = await ReadSettingsAsync(CacheFilePath, cancellationToken);
            cached.SharedSettingsPath = ActiveSharedSettingsPath;
            return cached;
        }

        return new AppSettings { SharedSettingsPath = ActiveSharedSettingsPath };
    }

    public async Task SaveAsync(AppSettings settings, string? sharedSettingsPath = null, CancellationToken cancellationToken = default)
    {
        ActiveSharedSettingsPath = string.IsNullOrWhiteSpace(sharedSettingsPath)
            ? settings.SharedSettingsPath
            : sharedSettingsPath.Trim();

        if (string.IsNullOrWhiteSpace(ActiveSharedSettingsPath))
        {
            throw new InvalidOperationException("Le chemin de la configuration partagée est obligatoire.");
        }

        settings.SharedSettingsPath = ActiveSharedSettingsPath;
        await WriteSettingsAtomicallyAsync(ActiveSharedSettingsPath, settings, cancellationToken, createBackup: true);
        await WriteSettingsAtomicallyAsync(CacheFilePath, settings, cancellationToken, createBackup: false);
        await WriteBootstrapAsync(cancellationToken);
        LastLoadUsedCache = false;
    }

    private async Task<string> LoadSharedPathAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(BootstrapFilePath)) return SettingsDefaults.SharedSettingsPath;
        await using var stream = File.OpenRead(BootstrapFilePath);
        var bootstrap = await JsonSerializer.DeserializeAsync<BootstrapSettings>(stream, JsonOptions, cancellationToken);
        return string.IsNullOrWhiteSpace(bootstrap?.SharedSettingsPath)
            ? SettingsDefaults.SharedSettingsPath
            : bootstrap.SharedSettingsPath;
    }

    private static async Task<AppSettings> ReadSettingsAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
            ?? new AppSettings();
    }

    private async Task WriteBootstrapAsync(CancellationToken cancellationToken)
    {
        var bootstrap = new BootstrapSettings { SharedSettingsPath = ActiveSharedSettingsPath };
        var directory = Path.GetDirectoryName(BootstrapFilePath)
            ?? throw new InvalidOperationException("Le chemin local de démarrage est invalide.");
        Directory.CreateDirectory(directory);
        var temp = BootstrapFilePath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, bootstrap, JsonOptions, cancellationToken);
        }
        File.Move(temp, BootstrapFilePath, true);
    }

    private static async Task WriteSettingsAtomicallyAsync(
        string path,
        AppSettings settings,
        CancellationToken cancellationToken,
        bool createBackup)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Le chemin de configuration est invalide.");
        Directory.CreateDirectory(directory);

        if (createBackup && File.Exists(path))
        {
            var backupDirectory = Path.Combine(directory, "Backups");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(
                backupDirectory,
                $"biblideo.settings.{DateTime.Now:yyyyMMdd-HHmmss-fff}.{Environment.MachineName}.json");
            File.Copy(path, backupPath, overwrite: false);
        }

        var tempPath = path + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }
        File.Move(tempPath, path, true);
    }

    private sealed class BootstrapSettings
    {
        public string SharedSettingsPath { get; set; } = SettingsDefaults.SharedSettingsPath;
    }
}
