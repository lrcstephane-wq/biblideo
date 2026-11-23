using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Biblideo.App.Services;

public sealed record NexusCustomer(Guid Id, string Name, long Revision);
public sealed record NexusCustomerSnapshot(int SchemaVersion, long Revision, DateTimeOffset GeneratedAtUtc, NexusCustomer[] Clients);
public sealed record NexusAtlasRight(string Id, Guid ClientId, string Customer, string DeviceId, DateOnly ValidUntil, bool IsCreator, long Revision)
{
    public string Offer => IsCreator ? "Créateur" : "Standard";
    public string Status => ValidUntil < DateOnly.FromDateTime(DateTime.Today) ? "Expirée" : "Valide";
}

internal sealed record NexusAtlasEvent(string Operation, DateTimeOffset RecordedAtUtc, NexusAtlasRight? Previous, NexusAtlasRight Current);

internal static class NexusRegistryClient
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        BaseAddress = new Uri("http://127.0.0.1:5078/"),
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string CachePath => Path.Combine(NexusPreviewProfile.Enabled
        ? NexusPreviewProfile.DataRoot
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ideo Solutions", "Nexus"),
        "clients.snapshot.json");

    public static async Task<NexusCustomerSnapshot> LoadCustomersAsync()
    {
        var snapshot = await ReadAsync<NexusCustomerSnapshot>("clients");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var temporary = CachePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
                File.Move(temporary, CachePath, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Une copie locale indisponible ne rend pas le registre en ligne inaccessible.
        }
        return snapshot;
    }

    public static async Task<NexusCustomerSnapshot?> ReadCachedCustomersAsync()
    {
        try
        {
            var data = await File.ReadAllTextAsync(CachePath);
            return JsonSerializer.Deserialize<NexusCustomerSnapshot>(data, JsonOptions) is { SchemaVersion: 1 } cached
                ? cached : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static Task<NexusAtlasRight[]> LoadAtlasAsync() => ReadAsync<NexusAtlasRight[]>("atlas-licenses");

    public static Task<NexusAtlasEvent[]> LoadAtlasHistoryAsync(string id) =>
        ReadAsync<NexusAtlasEvent[]>($"atlas-licenses/{Uri.EscapeDataString(id)}/history");

    public static Task<string> DownloadAtlasAsync(string id) =>
        Client.GetStringAsync($"atlas-licenses/{Uri.EscapeDataString(id)}/file");

    public static Task WriteAtlasAsync(string id, Guid clientId, string code, long revision, string operation) =>
        SendAsync(() => Client.PutAsJsonAsync($"atlas-licenses/{Uri.EscapeDataString(id)}", new { clientId, code, revision, operation }));

    public static Task SaveCustomerAsync(NexusCustomer? customer, string name) => SendAsync(() => customer is null
        ? Client.PostAsJsonAsync("clients", new { name })
        : Client.PutAsJsonAsync($"clients/{customer.Id:D}", new { name, customer.Revision }));

    private static async Task<T> ReadAsync<T>(string route) where T : class =>
        await Client.GetFromJsonAsync<T>(route) ?? throw new InvalidDataException("Réponse Nexus vide.");

    private static async Task SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        using var response = await send();
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ApiError>();
            throw new InvalidOperationException(body?.Error ?? "Modification refusée par le service.");
        }
        response.EnsureSuccessStatusCode();
    }

    private sealed record ApiError(string? Error);
}
