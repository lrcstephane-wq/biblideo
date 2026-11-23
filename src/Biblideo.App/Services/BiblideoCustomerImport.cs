using System.IO;
using System.Text.Json;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Services;

internal sealed record ImportedBiblideoCustomer(Guid Id, string Name);

internal static class BiblideoCustomerImport
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static async Task<(ImportedBiblideoCustomer[] Customers, int Skipped)> ReadAsync(ClientModuleContext context)
    {
        // Lecture seule : les fiches sans identifiant explicite ne doivent pas recevoir un nouvel ID à chaque import.
        await using var source = File.OpenRead(context.ClientRegistryPath);
        var registry = await JsonSerializer.DeserializeAsync<LegacyRegistry>(source, Options)
            ?? throw new InvalidDataException("Le registre Biblidéo est vide.");
        if (registry.SchemaVersion != 1 || registry.Clients is null)
            throw new InvalidDataException("La structure du registre Biblidéo n’est pas reconnue.");

        var result = new List<ImportedBiblideoCustomer>();
        var ids = new HashSet<Guid>();
        foreach (var entry in registry.Clients)
        {
            if (entry is null || entry.IsArchived || entry.Id == Guid.Empty || entry.FolderName is null
                || !ClientFolderNamingService.TryParse(entry.FolderName, context.ClientSubscriptionMarker, out var folder)
                || folder!.DisplayName.Length > 200)
                continue;

            if (!ids.Add(entry.Id))
                throw new InvalidDataException("Deux fiches Biblidéo partagent le même identifiant. Vérifie le registre avant de réessayer.");

            result.Add(new ImportedBiblideoCustomer(entry.Id, folder.DisplayName));
        }

        return (result.ToArray(), registry.Clients.Length - result.Count);
    }

    private sealed record LegacyRegistry(int SchemaVersion, LegacyCustomer?[]? Clients);
    private sealed record LegacyCustomer(Guid Id, string? FolderName, bool IsArchived);
}
