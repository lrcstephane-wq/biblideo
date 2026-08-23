using System.Text.Json;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class ClientRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ClientRegistry> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new ClientRegistry();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ClientRegistry>(stream, JsonOptions, cancellationToken)
               ?? new ClientRegistry();
    }

    public async Task SaveAsync(string path, ClientRegistry registry, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Le chemin du registre clients est invalide.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, registry, JsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, path, true);
    }

    public async Task AppendHistoryAsync(string path, ClientHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Le chemin de l’historique clients est invalide.");
        Directory.CreateDirectory(directory);
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(path, line, cancellationToken);
    }
}
