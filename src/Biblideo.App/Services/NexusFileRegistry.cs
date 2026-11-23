using System.IO;
using System.Text.Json;
using Biblideo.Core.Services;

namespace Biblideo.App.Services;

internal sealed class NexusFileRegistry(string root, bool readOnly, string? publicKey = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal const string DirectoryName = "Registre Nexus";
    internal const string RegistryName = "registre-nexus.json";

    private string Folder => Path.Combine(root, DirectoryName);
    private string RegistryPath => Path.Combine(Folder, RegistryName);
    private string BiblideoUsagePath => Path.Combine(Folder, "suivi-licences-biblideo.json");

    public async Task<ILookup<Guid, string>> ReadUnusedBiblideoKeysAsync() => (await ReadBiblideoUsageAsync())
        .GroupBy(entry => (entry.ClientId, entry.KeyId))
        .Where(group => group.Last().IsUnused)
        .ToLookup(group => group.Key.ClientId, group => group.Key.KeyId);

    public Task SetBiblideoUsageAsync(Guid clientId, string keyId, bool isUnused) =>
        MutateAsync(BiblideoUsagePath, ReadBiblideoUsageAsync, events =>
        {
            if (clientId == Guid.Empty || keyId.Length != 64 || !keyId.All(Uri.IsHexDigit))
                throw new InvalidOperationException("L’identifiant de licence Biblidéo est invalide.");

            var previous = events.LastOrDefault(entry => entry.ClientId == clientId && entry.KeyId == keyId);
            var current = previous?.IsUnused == true;
            if (current == isUnused) return null;

            events.Add(new(clientId, keyId, isUnused, DateTimeOffset.UtcNow, Environment.UserName));
            return events;
        });

    private async Task<List<BiblideoUsageEvent>> ReadBiblideoUsageAsync()
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("La racine du registre Nexus est inaccessible.");
        try
        {
            await using var source = new FileStream(BiblideoUsagePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<List<BiblideoUsageEvent>>(source, JsonOptions)
                ?? throw new InvalidDataException("Le suivi des licences Biblidéo est illisible.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }
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

    public Task WriteAtlasAsync(string id, NexusCustomer customer, string code, long revision, string operation)
    {
        var claims = AtlasLicenseCodec.Read(code, publicKey);
        if (claims.LicenseId != id || !Guid.TryParse(id, out _) || revision < 0)
            throw new InvalidOperationException("Identifiant de licence Atlas incorrect.");

        if (customer.Id == Guid.Empty || customer.Name != claims.Customer)
            throw new InvalidOperationException("La fiche client ne correspond pas au bénéficiaire signé.");

        return MutateAsync(data =>
        {
            var index = data.Atlas.FindIndex(entry => entry.Right.Id == id);
            var previous = index < 0 ? null : data.Atlas[index].Right;
            if ((previous?.Revision ?? 0) != revision)
                throw new InvalidOperationException("Cette licence a changé. Actualise la liste puis réessaie.");
            if (data.Atlas.Any(entry => entry.Right.Id != id && entry.Right.DeviceId == claims.DeviceId))
                throw new InvalidOperationException("Ce poste a déjà une licence Atlas.");

            var current = new NexusAtlasRight(id, customer.Id, claims.Customer, claims.DeviceId, claims.ValidUntil, claims.IsCreator, revision + 1);
            var allowed = previous is null
                ? operation == "create" && current.ValidUntil >= DateOnly.FromDateTime(DateTime.Today)
                : operation == "link"
                    ? previous.ClientId != current.ClientId && previous.DeviceId == current.DeviceId
                      && previous.IsCreator == current.IsCreator && previous.ValidUntil == current.ValidUntil
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

    public Task RemoveAtlasAsync(Guid clientId, IReadOnlyList<NexusAtlasRight> expected, bool entireCompany) => MutateAsync(data =>
    {
        if (expected.Count == 0 || (!entireCompany && expected.Count != 1)
            || expected.Any(right => right.ClientId != clientId))
            throw new InvalidOperationException("La sélection des licences Atlas est invalide.");

        var displayedRevisions = expected.Select(right => (right.Id, right.Revision)).ToHashSet();
        var current = data.Atlas.Where(entry => entry.Right.ClientId == clientId).Select(entry => entry.Right);
        var removed = entireCompany
            ? current.ToArray()
            : current.Where(right => right.Id == expected[0].Id).ToArray();

        if (displayedRevisions.Count != expected.Count
            || !displayedRevisions.SetEquals(removed.Select(right => (right.Id, right.Revision))))
            throw new InvalidOperationException("Les licences ont changé. Actualise le registre avant de réessayer.");

        var now = DateTimeOffset.UtcNow;
        data.Atlas.RemoveAll(entry => entry.Right.ClientId == clientId
            && displayedRevisions.Contains((entry.Right.Id, entry.Right.Revision)));
        data.Events.AddRange(removed.Select(right => new NexusAtlasEvent("remove", now, right, right)));
        return true;
    });

    private async Task<RegistryFile> ReadAsync()
    {
        try
        {
            await using var source = new FileStream(RegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var data = await JsonSerializer.DeserializeAsync<RegistryFile>(source, JsonOptions);
            if (data is not { SchemaVersion: 1 or 2, Atlas: not null, Events: not null })
                throw new InvalidDataException("Le format du registre Nexus est inconnu.");
            return data;
        }
        catch (Exception error) when ((error is FileNotFoundException or DirectoryNotFoundException) && !readOnly && Directory.Exists(root))
        {
            return new(2, 0, DateTimeOffset.MinValue, [], []);
        }
    }

    private Task MutateAsync(Func<RegistryFile, bool> change) =>
        MutateAsync(RegistryPath, ReadAsync,
            current => change(current) ? current with
            {
                SchemaVersion = 2, Revision = current.Revision + 1, UpdatedAtUtc = DateTimeOffset.UtcNow
            } : null,
            current => { if (current.SchemaVersion == 1) PreserveLegacyFile(); });

    private async Task MutateAsync<T>(string path, Func<Task<T>> read, Func<T, T?> change, Action<T>? beforeReplace = null)
        where T : class
    {
        if (readOnly) throw new InvalidOperationException("La copie cloud s’ouvre en lecture seule.");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("La racine réseau du registre Nexus est inaccessible.");
        Directory.CreateDirectory(Folder);

        await using var lease = await AcquireAsync();
        var current = await read();
        var next = change(current);
        if (next is null) return;
        var temp = Path.Combine(Folder, $"arrivage-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(destination, next, JsonOptions);
                await destination.FlushAsync();
            }

            beforeReplace?.Invoke(current);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private void PreserveLegacyFile()
    {
        var backup = Path.Combine(Folder, "registre-nexus-v1.bak");
        if (File.Exists(backup)) return;
        var temp = Path.Combine(Folder, $"arrivage-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(RegistryPath, temp);
            File.Move(temp, backup);
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

    // La version 1 contenait aussi une liste de clients. Les licences et événements restent compatibles.
    private sealed record RegistryFile(int SchemaVersion, long Revision, DateTimeOffset UpdatedAtUtc,
        List<StoredAtlasRight> Atlas, List<NexusAtlasEvent> Events);
    private sealed record BiblideoUsageEvent(Guid ClientId, string KeyId, bool IsUnused, DateTimeOffset RecordedAtUtc, string Operator);
    private sealed record StoredAtlasRight(NexusAtlasRight Right, string Code);
}
