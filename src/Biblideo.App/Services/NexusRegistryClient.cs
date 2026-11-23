using Biblideo.Core.Models;

namespace Biblideo.App.Services;

internal sealed record NexusCustomer(Guid Id, string Name);

public sealed record NexusAtlasRight(string Id, Guid ClientId, string Customer, string DeviceId, DateOnly ValidUntil, bool IsCreator, long Revision)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ClientLabel { get; init; }
    public string Offer => IsCreator ? "Créateur" : "Standard";
    public bool IsActiveOn(DateOnly date) => ValidUntil >= date;
    public string Status => IsActiveOn(DateOnly.FromDateTime(DateTime.Today)) ? "Valide" : "Expirée";
}

internal sealed record NexusAtlasEvent(string Operation, DateTimeOffset RecordedAtUtc, NexusAtlasRight? Previous, NexusAtlasRight Current);

internal static class NexusRegistryClient
{
    public static NexusRegistryLocation Location => NexusRegistryLocation.Load();

    private static NexusFileRegistry Open()
    {
        var location = Location;
        return location.ActiveRoot is { } root
            ? new NexusFileRegistry(root, location.UseCloud)
            : throw new InvalidOperationException("Renseigne l’emplacement du registre Atlas dans Paramètres.");
    }

    public static Task<NexusAtlasRight[]> LoadAtlasAsync() => Open().ReadAtlasAsync();
    public static Task<NexusAtlasEvent[]> LoadAtlasHistoryAsync(string id) => Open().ReadHistoryAsync(id);
    public static Task<string> DownloadAtlasAsync(string id) => Open().ReadCodeAsync(id);

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
