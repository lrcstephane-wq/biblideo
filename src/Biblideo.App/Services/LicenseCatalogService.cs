using System.IO;
using System.Text.Json;
using Biblideo.Core.Models;

namespace Biblideo.App.Services;

public sealed class LicenseCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly OutlookMsgReader _reader = new();

    public async Task<IReadOnlyList<ParsedLicenseMessage>> ReadAsync(IReadOnlyList<string> paths, string indexPath, CancellationToken cancellationToken = default)
    {
        var distinctPaths = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var index = await LoadAsync(indexPath, cancellationToken);
        var cachedByPath = index.Entries.ToDictionary(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase);
        var results = new List<ParsedLicenseMessage>(distinctPaths.Length);
        var pending = new List<string>();
        foreach (var path in distinctPaths)
        {
            var info = new FileInfo(path);
            if (cachedByPath.TryGetValue(path, out var cached) && string.IsNullOrWhiteSpace(cached.Error) && cached.Length == info.Length && cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
            {
                results.Add(ToMessage(cached));
            }
            else pending.Add(path);
        }

        if (pending.Count > 0)
            results.AddRange(await _reader.ReadManyAsync(pending));

        var refreshedEntries = results.Select(message =>
        {
            var info = new FileInfo(message.FullPath);
            return new LicenseIndexEntry
            {
                FullPath = message.FullPath,
                Length = info.Exists ? info.Length : 0,
                LastWriteUtcTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                Subject = message.Subject,
                Recipients = message.Recipients.ToList(),
                ExpiryDate = message.ExpiryDate,
                Error = message.Error
            };
        }).ToArray();
        var requestedPaths = distinctPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        index.Entries = index.Entries
            .Where(entry => !requestedPaths.Contains(entry.FullPath) && File.Exists(entry.FullPath))
            .Concat(refreshedEntries)
            .DistinctBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await SaveAsync(indexPath, index, cancellationToken);
        return results.OrderBy(message => message.FileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task AppendHistoryAsync(string path, LicenseHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Chemin du journal licences invalide.");
        Directory.CreateDirectory(directory);
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, cancellationToken);
    }

    private static ParsedLicenseMessage ToMessage(LicenseIndexEntry entry) => new()
    {
        FullPath = entry.FullPath,
        FileName = Path.GetFileName(entry.FullPath),
        Subject = entry.Subject,
        Recipients = entry.Recipients,
        ExpiryDate = entry.ExpiryDate,
        Error = entry.Error
    };

    private static async Task<LicenseIndex> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new LicenseIndex();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LicenseIndex>(stream, JsonOptions, cancellationToken) ?? new LicenseIndex();
    }

    private static async Task SaveAsync(string path, LicenseIndex index, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Chemin de l’index licences invalide.");
        Directory.CreateDirectory(directory);
        var temporary = path + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, index, JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
    }
}
