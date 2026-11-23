using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexus.Service;

internal sealed class SharedAccess
{
    private readonly TeamAccess[] _accounts;

    private SharedAccess(TeamAccess[] accounts) => _accounts = accounts;

    public static SharedAccess Load(string path)
    {
        var records = JsonSerializer.Deserialize<TeamAccessFile>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Accounts
            ?? throw new InvalidDataException("Le fichier des accès Nexus ne contient aucun compte.");

        if (records.Length == 0 || records.Any(account => account is null
                || string.IsNullOrWhiteSpace(account.Name)
                || account.Role is not ("Reader" or "Editor" or "Administrator")
                || !IsHexSecret(account.TokenSha256))
            || records.Select(account => account!.TokenSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() != records.Length
            || records.Select(account => account!.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != records.Length
            || !records.Any(account => account!.Role == "Administrator"))
            throw new InvalidDataException("Les comptes Nexus doivent avoir un nom, un rôle et une empreinte uniques, avec au moins un administrateur.");

        return new SharedAccess(records.Select(account => new TeamAccess(account!.Name.Trim(), account.Role,
            Convert.FromHexString(account.TokenSha256))).ToArray());
    }

    public async Task AuthorizeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers.CacheControl = "no-store";
        var secret = context.Request.Headers.Authorization.ToString();
        TeamAccess? account = null;
        if (secret.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && IsHexSecret(secret[7..]))
        {
            var digest = SHA256.HashData(Encoding.ASCII.GetBytes(secret[7..]));
            account = _accounts.FirstOrDefault(candidate => CryptographicOperations.FixedTimeEquals(candidate.Digest, digest));
        }

        if (account is null)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        var defaultPermission = HttpMethods.IsGet(context.Request.Method) ? NexusPermission.Read : NexusPermission.Administer;
        var required = context.GetEndpoint()?.Metadata.OfType<NexusPermission>().LastOrDefault(defaultPermission)
            ?? defaultPermission;
        var allowed = required switch
        {
            NexusPermission.Read => true,
            NexusPermission.Edit => account.Role is "Editor" or "Administrator",
            NexusPermission.Administer => account.Role == "Administrator",
            _ => false
        };
        if (!allowed)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        context.Items[typeof(TeamAccess)] = account;
        await next(context);
    }

    internal static TeamAccess? GetAccount(HttpContext context) => context.Items[typeof(TeamAccess)] as TeamAccess;

    private static bool IsHexSecret(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record TeamAccessFile(TeamAccessEntry?[] Accounts);
    private sealed record TeamAccessEntry(string Name, string Role, string TokenSha256);
}

internal sealed record TeamAccess(string Name, string Role, byte[] Digest);

internal enum NexusPermission { Read, Edit, Administer }
