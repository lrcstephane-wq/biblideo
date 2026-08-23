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
    public string MachinePathsFilePath { get; }
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
        MachinePathsFilePath = Path.Combine(root, "machine-paths.json");
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
            return await ApplyMachinePathsAsync(shared, cancellationToken);
        }

        if (File.Exists(CacheFilePath))
        {
            LastLoadUsedCache = true;
            var cached = await ReadSettingsAsync(CacheFilePath, cancellationToken);
            cached.SharedSettingsPath = ActiveSharedSettingsPath;
            return await ApplyMachinePathsAsync(cached, cancellationToken);
        }

        return await ApplyMachinePathsAsync(
            new AppSettings { SharedSettingsPath = ActiveSharedSettingsPath },
            cancellationToken);
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
        await WriteMachinePathsAsync(settings, cancellationToken);
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

    private async Task<AppSettings> ApplyMachinePathsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(MachinePathsFilePath)) return settings;
        await using var stream = File.OpenRead(MachinePathsFilePath);
        var machinePaths = await JsonSerializer.DeserializeAsync<MachinePathSettings>(stream, JsonOptions, cancellationToken);
        machinePaths?.ApplyTo(settings);
        return settings;
    }

    private async Task WriteMachinePathsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(MachinePathsFilePath)
            ?? throw new InvalidOperationException("Le chemin des réglages de ce poste est invalide.");
        Directory.CreateDirectory(directory);
        var temp = MachinePathsFilePath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, MachinePathSettings.From(settings), JsonOptions, cancellationToken);
        }
        File.Move(temp, MachinePathsFilePath, true);
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

    private sealed class MachinePathSettings
    {
        public string SourceScPath { get; set; } = string.Empty;
        public string SourceEpPath { get; set; } = string.Empty;
        public string ClientsRootPath { get; set; } = string.Empty;
        public string IdeoSolutionsRootPath { get; set; } = string.Empty;
        public string ProductStagingPath { get; set; } = string.Empty;
        public string ProductArchivesPath { get; set; } = string.Empty;
        public string ProductTrashPath { get; set; } = string.Empty;
        public string ReportsPath { get; set; } = string.Empty;
        public string MonthlyPackagesRootPath { get; set; } = string.Empty;
        public string SevenZipExecutablePath { get; set; } = string.Empty;
        public string LicenseInboxPath { get; set; } = string.Empty;
        public string LicenseIndexPath { get; set; } = string.Empty;
        public string LicenseHistoryPath { get; set; } = string.Empty;

        public static MachinePathSettings From(AppSettings settings) => new()
        {
            SourceScPath = settings.SourceScPath,
            SourceEpPath = settings.SourceEpPath,
            ClientsRootPath = settings.ClientsRootPath,
            IdeoSolutionsRootPath = settings.IdeoSolutionsRootPath,
            ProductStagingPath = settings.ProductStagingPath,
            ProductArchivesPath = settings.ProductArchivesPath,
            ProductTrashPath = settings.ProductTrashPath,
            ReportsPath = settings.ReportsPath,
            MonthlyPackagesRootPath = settings.MonthlyPackagesRootPath,
            SevenZipExecutablePath = settings.SevenZipExecutablePath,
            LicenseInboxPath = settings.LicenseInboxPath,
            LicenseIndexPath = settings.LicenseIndexPath,
            LicenseHistoryPath = settings.LicenseHistoryPath
        };

        public void ApplyTo(AppSettings settings)
        {
            settings.SourceScPath = SourceScPath;
            settings.SourceEpPath = SourceEpPath;
            settings.ClientsRootPath = ClientsRootPath;
            settings.IdeoSolutionsRootPath = IdeoSolutionsRootPath;
            settings.ProductStagingPath = ProductStagingPath;
            settings.ProductArchivesPath = ProductArchivesPath;
            settings.ProductTrashPath = ProductTrashPath;
            settings.ReportsPath = ReportsPath;
            settings.MonthlyPackagesRootPath = MonthlyPackagesRootPath;
            settings.SevenZipExecutablePath = SevenZipExecutablePath;
            settings.LicenseInboxPath = LicenseInboxPath;
            settings.LicenseIndexPath = LicenseIndexPath;
            settings.LicenseHistoryPath = LicenseHistoryPath;
        }
    }
}
