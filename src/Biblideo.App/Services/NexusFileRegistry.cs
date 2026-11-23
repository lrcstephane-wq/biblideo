using System.IO;
using System.Text.Json;
using Biblideo.Core.Services;

namespace Biblideo.App.Services;

internal sealed class NexusFileRegistry(string root, bool readOnly)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal const string DirectoryName = "Registre Nexus";
    internal const string RegistryName = "registre-nexus.json";

    private string Folder => Path.Combine(root, DirectoryName);
    private string RegistryPath => Path.Combine(Folder, RegistryName);

    public async Task<NexusCustomerSnapshot> ReadCustomersAsync()
    {
        var data = await ReadAsync();
        return new(1, data.Revision, data.UpdatedAtUtc, data.Clients.OrderBy(client => client.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }

    public async Task<NexusAtlasRight[]> ReadAtlasAsync() => (await ReadAsync()).Atlas
        .Select(entry => entry.Right).OrderBy(right => right.DeviceId, StringComparer.Ordinal).ToArray();

    public async Task<NexusAtlasEvent[]> ReadHistoryAsync(string id) => (await ReadAsync()).Events
        .Where(entry => entry.Current.Id == id).Reverse().ToArray();

    public async Task<string> ReadCodeAsync(string id)
    {
        if (readOnly) throw new InvalidOperationException("L’export s’effectue depuis le registre du NAS.");
        return (await ReadAsync()).Atlas.SingleOrDefault(entry => entry.Right.Id == id)?.Code
            ?? throw new InvalidOperationException("La licence n’existe plus dans le registre.");
    }

    public Task SaveCustomerAsync(NexusCustomer? previous, string name) => MutateAsync(data =>
    {
        name = CheckedName(name);
        if (previous is null)
        {
            data.Clients.Add(new(Guid.NewGuid(), name, 1));
        }
        else
        {
            var index = data.Clients.FindIndex(client => client.Id == previous.Id);
            if (index < 0 || data.Clients[index].Revision != previous.Revision)
                throw new InvalidOperationException("Cette fiche a changé. Actualise la liste puis réessaie.");
            if (data.Clients[index].Name == name) return false;
            data.Clients[index] = previous with { Name = name, Revision = previous.Revision + 1 };
        }
        return true;
    });

    public async Task<NexusRegistryClient.CustomerImportResult> ImportAsync(ImportedBiblideoCustomer[] customers)
    {
        var added = 0;
        await MutateAsync(data =>
        {
            var ids = data.Clients.Select(client => client.Id).ToHashSet();
            foreach (var customer in customers)
            {
                if (!ids.Add(customer.Id)) continue;
                data.Clients.Add(new(customer.Id, CheckedName(customer.Name), 1));
                added++;
            }
            return added > 0;
        });
        return new(added, customers.Length - added);
    }

    public Task WriteAtlasAsync(string id, Guid clientId, string code, long revision, string operation)
    {
        var claims = AtlasLicenseCodec.Read(code);
        if (claims.LicenseId != id || !Guid.TryParse(id, out _) || revision < 0)
            throw new InvalidOperationException("Identifiant de licence Atlas incorrect.");

        return MutateAsync(data =>
        {
            if (data.Clients.SingleOrDefault(client => client.Id == clientId)?.Name != claims.Customer)
                throw new InvalidOperationException("La fiche client a changé ou ne correspond pas à la licence.");

            var index = data.Atlas.FindIndex(entry => entry.Right.Id == id);
            var previous = index < 0 ? null : data.Atlas[index].Right;
            if ((previous?.Revision ?? 0) != revision)
                throw new InvalidOperationException("Cette licence a changé. Actualise la liste puis réessaie.");
            if (data.Atlas.Any(entry => entry.Right.Id != id && entry.Right.DeviceId == claims.DeviceId))
                throw new InvalidOperationException("Ce poste a déjà une licence Atlas.");

            var current = new NexusAtlasRight(id, clientId, claims.Customer, claims.DeviceId, claims.ValidUntil, claims.IsCreator, revision + 1);
            var allowed = previous is null
                ? operation == "create" && current.ValidUntil >= DateOnly.FromDateTime(DateTime.Today)
                : previous.ClientId == current.ClientId && operation switch
                {
                    "renew" => current.DeviceId == previous.DeviceId && current.IsCreator == previous.IsCreator && current.ValidUntil > previous.ValidUntil,
                    "upgrade" => !previous.IsCreator && current.IsCreator && current.DeviceId == previous.DeviceId && current.ValidUntil == previous.ValidUntil,
                    "transfer" => current.DeviceId != previous.DeviceId && current.IsCreator == previous.IsCreator && current.ValidUntil == previous.ValidUntil,
                    _ => false
                };
            if (!allowed)
                throw new InvalidOperationException("L’opération ne correspond pas aux droits Atlas enregistrés.");

            var entry = new StoredAtlasRight(current, code);
            if (index < 0) data.Atlas.Add(entry);
            else data.Atlas[index] = entry;
            data.Events.Add(new(operation, DateTimeOffset.UtcNow, previous, current));
            return true;
        });
    }

    private async Task<RegistryFile> ReadAsync()
    {
        try
        {
            await using var source = new FileStream(RegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var data = await JsonSerializer.DeserializeAsync<RegistryFile>(source, JsonOptions);
            if (data is not { SchemaVersion: 1, Clients: not null, Atlas: not null, Events: not null })
                throw new InvalidDataException("Le format du registre Nexus est inconnu.");
            return data;
        }
        catch (Exception error) when ((error is FileNotFoundException or DirectoryNotFoundException) && !readOnly && Directory.Exists(root))
        {
            return new(1, 0, DateTimeOffset.MinValue, [], [], []);
        }
    }

    private async Task MutateAsync(Func<RegistryFile, bool> change)
    {
        if (readOnly) throw new InvalidOperationException("La copie cloud s’ouvre en lecture seule.");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("La racine réseau du registre Nexus est inaccessible.");
        Directory.CreateDirectory(Folder);

        await using var lease = await AcquireAsync();
        var current = await ReadAsync();
        if (!change(current)) return;

        var next = current with { Revision = current.Revision + 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
        var temp = Path.Combine(Folder, $"arrivage-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(destination, next, JsonOptions);
                await destination.FlushAsync();
            }

            File.Move(temp, RegistryPath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private async Task<FileStream> AcquireAsync()
    {
        var path = Path.Combine(Folder, "registre-nexus.lock");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 12)
            {
                await Task.Delay(125);
            }
        }
    }

    private static string CheckedName(string name) => name.Trim() is { Length: > 0 and <= 200 } valid
        ? valid : throw new ArgumentException("Le nom doit contenir de 1 à 200 caractères.");

    private sealed record RegistryFile(int SchemaVersion, long Revision, DateTimeOffset UpdatedAtUtc,
        List<NexusCustomer> Clients, List<StoredAtlasRight> Atlas, List<NexusAtlasEvent> Events);
    private sealed record StoredAtlasRight(NexusAtlasRight Right, string Code);
}
