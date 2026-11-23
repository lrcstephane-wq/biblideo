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

internal sealed record NexusSession(string Name, string Role)
{
    public bool CanEdit => Role is "Editor" or "Administrator";
    public bool CanAdminister => Role == "Administrator";
}

internal static class NexusRegistryClient
{
    private static readonly HttpClient LocalClient = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        BaseAddress = new Uri("http://127.0.0.1:5078/"),
        Timeout = TimeSpan.FromSeconds(4)
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly bool LocalDevelopment = NexusPreviewProfile.Enabled
        || Environment.GetEnvironmentVariable("IDEO_NEXUS_LOCAL_DEV") == "1";

    private static void RequireLocalDevelopment()
    {
        if (!LocalDevelopment)
            throw new InvalidOperationException("Renseigne la racine du registre dans Paramètres.");
    }

    private static NexusFileRegistry? FileRegistry
    {
        get
        {
            var location = NexusRegistryLocation.Load();
            return location.ActiveRoot is { } root ? new NexusFileRegistry(root, location.UseCloud) : null;
        }
    }

    public static bool IsShared => FileRegistry is not null;

    public static Task<NexusSession> GetSessionAsync()
    {
        var location = NexusRegistryLocation.Load();
        if (location.ActiveRoot is not null)
            return Task.FromResult(location.UseCloud ? new NexusSession("Copie cloud · Lecture seule", "Reader") : new NexusSession("NAS", "Administrator"));
        return ReadAsync<NexusSession>("session");
    }

    public static bool ConnectionUnavailable(Exception error) => error is OperationCanceledException
        || error is HttpRequestException { StatusCode: null, HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError };

    private static string CachePath => Path.Combine(NexusPreviewProfile.Enabled
        ? NexusPreviewProfile.DataRoot
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ideo Solutions", "Nexus"),
        "cache", "local", "clients.snapshot.json");

    public static async Task<NexusCustomerSnapshot> LoadCustomersAsync()
    {
        if (FileRegistry is { } files) return await files.ReadCustomersAsync();
        var snapshot = await ReadAsync<NexusCustomerSnapshot>("clients");
        var cachePath = CachePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var temporary = cachePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
                File.Move(temporary, cachePath, true);
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
            if (!LocalDevelopment || FileRegistry is not null) return null;
            var data = await File.ReadAllTextAsync(CachePath);
            return JsonSerializer.Deserialize<NexusCustomerSnapshot>(data, JsonOptions) is { SchemaVersion: 1 } cached
                ? cached : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static Task<NexusAtlasRight[]> LoadAtlasAsync() => FileRegistry is { } files
        ? files.ReadAtlasAsync() : ReadAsync<NexusAtlasRight[]>("atlas-licenses");

    public static Task<NexusAtlasEvent[]> LoadAtlasHistoryAsync(string id) =>
        FileRegistry is { } files ? files.ReadHistoryAsync(id)
            : ReadAsync<NexusAtlasEvent[]>($"atlas-licenses/{Uri.EscapeDataString(id)}/history");

    public static async Task<string> DownloadAtlasAsync(string id)
    {
        if (FileRegistry is { } files) return await files.ReadCodeAsync(id);
        using var response = await RequestAsync(HttpMethod.Get, $"atlas-licenses/{Uri.EscapeDataString(id)}/file");
        await CheckResponseAsync(response);
        return await response.Content.ReadAsStringAsync();
    }

    public static Task WriteAtlasAsync(string id, Guid clientId, string code, long revision, string operation) =>
        FileRegistry is { } files ? files.WriteAtlasAsync(id, clientId, code, revision, operation)
        : SendAsync(HttpMethod.Put, $"atlas-licenses/{Uri.EscapeDataString(id)}", new { clientId, code, revision, operation });

    public static async Task<CustomerImportResult> ImportCustomersAsync(ImportedBiblideoCustomer[] customers)
    {
        if (FileRegistry is { } files) return await files.ImportAsync(customers);
        using var response = await RequestAsync(HttpMethod.Post, "clients/import-biblideo", JsonContent.Create(customers));
        await CheckResponseAsync(response);
        return await response.Content.ReadFromJsonAsync<CustomerImportResult>()
            ?? throw new InvalidDataException("Résultat d’import indisponible.");
    }

    public static Task SaveCustomerAsync(NexusCustomer? customer, string name) => FileRegistry is { } files
        ? files.SaveCustomerAsync(customer, name)
        : customer is null
        ? SendAsync(HttpMethod.Post, "clients", new { name })
        : SendAsync(HttpMethod.Put, $"clients/{customer.Id:D}", new { name, customer.Revision });

    private static async Task<T> ReadAsync<T>(string route) where T : class
    {
        using var response = await RequestAsync(HttpMethod.Get, route);
        await CheckResponseAsync(response);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidDataException("Réponse Nexus vide.");
    }

    private static async Task SendAsync<T>(HttpMethod method, string route, T payload)
    {
        using var response = await RequestAsync(method, route, JsonContent.Create(payload));
        await CheckResponseAsync(response);
    }

    private static async Task<HttpResponseMessage> RequestAsync(HttpMethod method, string route, HttpContent? content = null)
    {
        RequireLocalDevelopment();
        using var request = new HttpRequestMessage(method, route) { Content = content };
        return await LocalClient.SendAsync(request);
    }

    private static async Task CheckResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Jeton Nexus refusé. Vérifie ta connexion.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Ton compte Nexus n’est pas autorisé à effectuer cette opération.");
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ApiError>();
            throw new InvalidOperationException(body?.Error ?? "Modification refusée par le service.");
        }
        response.EnsureSuccessStatusCode();
    }

    internal sealed record CustomerImportResult(int Added, int Existing);

    private sealed record ApiError(string? Error);
}
