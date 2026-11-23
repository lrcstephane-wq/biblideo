using System.IO;
using System.Text.Json;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Services;

internal static class BiblideoClientDirectory
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<NexusCustomer[]> ReadAsync(ClientModuleContext context)
    {
        if (!Directory.Exists(context.ClientsRootPath))
            throw new DirectoryNotFoundException("La racine des dossiers clients est inaccessible.");

        await using var source = new FileStream(context.ClientRegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var registry = await JsonSerializer.DeserializeAsync<DirectoryRegistry>(source, Options)
            ?? throw new InvalidDataException("Le registre clients est vide.");
        if (registry.SchemaVersion != 1 || registry.Clients is null)
            throw new InvalidDataException("La structure du registre clients n’est pas reconnue.");

        var result = new List<NexusCustomer>();
        var ids = new HashSet<Guid>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in registry.Clients)
        {
            if (entry is null || entry.IsArchived || entry.Id == Guid.Empty || entry.FolderName is null
                || entry.FolderName.Contains('/') || entry.FolderName.Contains('\\')
                || context.ClientIgnoredFolderNames.Contains(entry.FolderName, StringComparer.OrdinalIgnoreCase)
                || !ClientFolderNamingService.TryParse(entry.FolderName, context.ClientSubscriptionMarker, out var folder)
                || !Directory.Exists(Path.Combine(context.ClientsRootPath, entry.FolderName)))
                continue;

            if (!ids.Add(entry.Id) || !folders.Add(entry.FolderName))
                throw new InvalidDataException("Le registre contient un identifiant ou un dossier client en double.");

            result.Add(new NexusCustomer(entry.Id, folder!.DisplayName));
        }

        return result.OrderBy(client => client.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private sealed record DirectoryRegistry(int SchemaVersion, DirectoryCustomer?[]? Clients);
    private sealed record DirectoryCustomer(Guid Id, string? FolderName, bool IsArchived);
}
