using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly HttpClient Client = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };
    private static readonly HttpClient LocalClient = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(4)
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static Connection? _connection = NexusPreviewProfile.Enabled
        || Environment.GetEnvironmentVariable("IDEO_NEXUS_LOCAL_DEV") == "1"
        ? new(new Uri("http://127.0.0.1:5078/"), null, "local") : null;

    public static void Disconnect() => _connection = null;

    public static bool IsShared => _connection?.Token is not null;
    public static string? Address => _connection?.Token is not null ? _connection.Address.AbsoluteUri : null;

    public static void Configure(string address, string token)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Indique une adresse HTTPS Nexus valide.");
        token = token.Trim();
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new ArgumentException("Le jeton Nexus doit contenir 64 caractères hexadécimaux.");

        var uri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        var cacheScope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{uri}\n{token}")));
        _connection = new Connection(uri, token, cacheScope);
    }

    public static Task<NexusSession> GetSessionAsync() => ReadAsync<NexusSession>(GetConnection(), "session");

    public static bool ConnectionUnavailable(Exception error) => error is OperationCanceledException
        || error is HttpRequestException { StatusCode: null, HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError };

    private static string CachePath(Connection connection) => Path.Combine(NexusPreviewProfile.Enabled
        ? NexusPreviewProfile.DataRoot
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ideo Solutions", "Nexus"),
        "cache", connection.CacheScope, "clients.snapshot.json");

    public static async Task<NexusCustomerSnapshot> LoadCustomersAsync()
    {
        var connection = GetConnection();
        var snapshot = await ReadAsync<NexusCustomerSnapshot>(connection, "clients");
        var cachePath = CachePath(connection);
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
            if (_connection is not { } connection) return null;
            var data = await File.ReadAllTextAsync(CachePath(connection));
            return JsonSerializer.Deserialize<NexusCustomerSnapshot>(data, JsonOptions) is { SchemaVersion: 1 } cached
                ? cached : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static Task<NexusAtlasRight[]> LoadAtlasAsync() => ReadAsync<NexusAtlasRight[]>(GetConnection(), "atlas-licenses");

    public static Task<NexusAtlasEvent[]> LoadAtlasHistoryAsync(string id) =>
        ReadAsync<NexusAtlasEvent[]>(GetConnection(), $"atlas-licenses/{Uri.EscapeDataString(id)}/history");

    public static async Task<string> DownloadAtlasAsync(string id)
    {
        using var response = await RequestAsync(GetConnection(), HttpMethod.Get, $"atlas-licenses/{Uri.EscapeDataString(id)}/file");
        await CheckResponseAsync(response);
        return await response.Content.ReadAsStringAsync();
    }

    public static Task WriteAtlasAsync(string id, Guid clientId, string code, long revision, string operation) =>
        SendAsync(HttpMethod.Put, $"atlas-licenses/{Uri.EscapeDataString(id)}", new { clientId, code, revision, operation });

    public static async Task<CustomerImportResult> ImportCustomersAsync(ImportedBiblideoCustomer[] customers)
    {
        using var response = await RequestAsync(GetConnection(), HttpMethod.Post, "clients/import-biblideo", JsonContent.Create(customers));
        await CheckResponseAsync(response);
        return await response.Content.ReadFromJsonAsync<CustomerImportResult>()
            ?? throw new InvalidDataException("Résultat d’import indisponible.");
    }

    public static Task SaveCustomerAsync(NexusCustomer? customer, string name) => customer is null
        ? SendAsync(HttpMethod.Post, "clients", new { name })
        : SendAsync(HttpMethod.Put, $"clients/{customer.Id:D}", new { name, customer.Revision });

    private static async Task<T> ReadAsync<T>(Connection connection, string route) where T : class
    {
        using var response = await RequestAsync(connection, HttpMethod.Get, route);
        await CheckResponseAsync(response);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidDataException("Réponse Nexus vide.");
    }

    private static async Task SendAsync<T>(HttpMethod method, string route, T payload)
    {
        using var response = await RequestAsync(GetConnection(), method, route, JsonContent.Create(payload));
        await CheckResponseAsync(response);
    }

    private static async Task<HttpResponseMessage> RequestAsync(Connection connection, HttpMethod method, string route, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(connection.Address, route)) { Content = content };
        if (connection.Token is { } token)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await (connection.Token is null ? LocalClient : Client).SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized && ReferenceEquals(_connection, connection))
            Disconnect();
        return response;
    }

    private static Connection GetConnection() => _connection
        ?? throw new InvalidOperationException("Ouvre Connexion Nexus pour renseigner le serveur et ton jeton.");

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

    private sealed record Connection(Uri Address, string? Token, string CacheScope);
    private sealed record ApiError(string? Error);
}
