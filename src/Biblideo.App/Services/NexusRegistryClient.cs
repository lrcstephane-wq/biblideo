using System.IO;
using System.Text.Json;
using Biblideo.Core.Models;

namespace Biblideo.App.Services;

internal sealed record NexusCustomer(Guid Id, string Name);

public sealed record NexusAtlasRight(string Id, Guid ClientId, string Customer, string DeviceId, DateOnly ValidUntil, bool IsCreator, long Revision)
{
    public string Offer => IsCreator ? "Créateur" : "Standard";
    public bool IsActiveOn(DateOnly date) => ValidUntil >= date;
    public string Status => IsActiveOn(DateOnly.FromDateTime(DateTime.Today)) ? "Valide" : "Expirée";
}

internal sealed record NexusAtlasEvent(string Operation, DateTimeOffset RecordedAtUtc, NexusAtlasRight? Previous, NexusAtlasRight Current);

internal sealed record NexusBiblideoUsage(ILookup<Guid, string> UnusedKeys, bool CanEdit, string? Notice);

internal static class NexusRegistryClient
{
    public static NexusRegistryLocation Location => NexusRegistryLocation.Load();

    private static NexusFileRegistry Open()
    {
        var location = Location;
        return location.ActiveRoot is { } root
            ? new NexusFileRegistry(root, location.UseCloud)
            : throw new InvalidOperationException("Renseigne l’emplacement du registre Nexus dans Paramètres.");
    }

    public static async Task<NexusBiblideoUsage> LoadBiblideoUsageAsync()
    {
        try
        {
            var location = Location;
            if (location.ActiveRoot is { } root)
            {
                var unused = await new NexusFileRegistry(root, location.UseCloud).ReadUnusedBiblideoKeysAsync();
                return new(unused, !location.UseCloud, location.UseCloud ? "Copie cloud · lecture seule" : null);
            }
            return EmptyBiblideoUsage("Configure le registre Nexus pour modifier l’utilisation des clés.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            return EmptyBiblideoUsage("Suivi partagé indisponible · les clés inutilisées ne peuvent pas être identifiées.");
        }
    }

    private static NexusBiblideoUsage EmptyBiblideoUsage(string notice) =>
        new(Array.Empty<string>().ToLookup(_ => Guid.Empty), false, notice);

    public static async Task SetBiblideoUsageAsync(ClientModuleContext context, Guid clientId, string keyId, bool isUnused)
    {
        var registry = Open();
        var customers = await BiblideoClientDirectory.ReadAsync(context);
        if (!customers.Any(customer => customer.Id == clientId))
            throw new InvalidOperationException("La fiche client a changé. Actualise les clients puis réessaie.");
        await registry.SetBiblideoUsageAsync(clientId, keyId, isUnused);
    }

    public static Task<NexusAtlasRight[]> LoadAtlasAsync() => Open().ReadAtlasAsync();
    public static Task<NexusAtlasEvent[]> LoadAtlasHistoryAsync(string id) => Open().ReadHistoryAsync(id);
    public static Task<string> DownloadAtlasAsync(string id) => Open().ReadCodeAsync(id);

    public static Task RemoveAtlasAsync(Guid clientId, IReadOnlyList<NexusAtlasRight> expected, bool entireCompany) =>
        Open().RemoveAtlasAsync(clientId, expected, entireCompany);

    public static async Task WriteAtlasAsync(ClientModuleContext context, string id, NexusCustomer customer,
        string code, long revision, string operation)
    {
        var registry = Open();
        var customers = await BiblideoClientDirectory.ReadAsync(context);
        if (!customers.Contains(customer))
            throw new InvalidOperationException("La fiche client ou son dossier a changé. Actualise avant de signer.");
        if (operation == "link"
            && (await registry.ReadAtlasAsync()).Any(right => right.Id == id && customers.Any(client => client.Id == right.ClientId)))
            throw new InvalidOperationException("La licence est déjà rattachée à une fiche active.");

        await registry.WriteAtlasAsync(id, customer, code, revision, operation);
    }
}
