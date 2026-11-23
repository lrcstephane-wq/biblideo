using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Biblideo.Core.Services;

// Format ATL1 du dépôt Atlas, AtlasLicenseService, commit ae70d12.
public static partial class AtlasLicenseCodec
{
    public const string PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEDpV+t9vu4eMTZL7zR9dCWuAXDNbZ
        QqhUAjnRYh2sf+RzWiITQinOmMWI9zk76jAEAzxxwKomdt01RDWnXjkXIg==
        -----END PUBLIC KEY-----
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string NormalizeDevice(string raw)
    {
        var value = raw.Trim().ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        if (!DevicePattern().IsMatch(value)) throw new ArgumentException("L’identifiant doit correspondre au code poste affiché dans Atlas.");
        return string.Join('-', "ATL", value[3..7], value[7..11], value[11..15], value[15..19], value[19..23]);
    }

    public static string Sign(AtlasLicenseClaims claims, string privateKey, string? publicKey = null)
    {
        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKey);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Payload(claims.Customer, claims.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            claims.LicenseId, NormalizeDevice(claims.DeviceId), claims.IsCreator ? ["creator"] : []));
        var code = $"ATL1.{Encode(bytes)}.{Encode(signer.SignData(bytes, HashAlgorithmName.SHA256))}";
        _ = Read(code, publicKey);
        return code;
    }

    public static AtlasLicenseClaims Read(string code, string? publicKey = null)
    {
        if (code.Length > 8192) throw new FormatException("Le code Atlas est trop long.");
        var parts = code.Trim().Split('.');
        if (parts is not ["ATL1", var payload, var signature]) throw new FormatException("Le format de licence attendu est ATL1.");
        var bytes = Decode(payload);
        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(publicKey ?? PublicKey);
        if (!verifier.VerifyData(bytes, Decode(signature), HashAlgorithmName.SHA256))
            throw new CryptographicException("La signature ne correspond pas à la clé publique Atlas.");
        var data = JsonSerializer.Deserialize<Payload>(bytes, JsonOptions);
        if (data is null || string.IsNullOrWhiteSpace(data.Customer) || string.IsNullOrWhiteSpace(data.LicenseId)
            || data.DeviceId is null || !DateOnly.TryParseExact(data.ValidUntil, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new FormatException("Champs Atlas invalides ou licence non liée à un poste.");
        return new(data.Customer.Trim(), data.LicenseId.Trim(), NormalizeDevice(data.DeviceId), date,
            (data.Features ?? []).Any(feature => string.Equals(feature?.Trim(), "creator", StringComparison.OrdinalIgnoreCase)));
    }

    // Protocole IdeoAdminAccessService du dépôt Atlas (ae70d12).
    public static string SignAdminResponse(string requestCode, string operatorName, string privateKey,
        DateTimeOffset? now = null, string? publicKey = null)
    {
        var name = operatorName.Trim();
        if (name.Length is < 1 or > 120)
            throw new ArgumentException("Renseigne le nom de l’opérateur Idéo (120 caractères au maximum).");
        if (requestCode.Length > 8192 || requestCode.Trim().Split('.') is not ["ATLREQ1", var encodedRequest])
            throw new FormatException("Colle la demande d’accès affichée par Atlas.");

        var request = JsonSerializer.Deserialize<AdminRequest>(Decode(encodedRequest), JsonOptions);
        if (request is null || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.Nonce))
            throw new FormatException("La demande d’accès Atlas est incomplète.");
        _ = NormalizeDevice(request.DeviceId);
        var current = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (request.CreatedUtc > current.AddMinutes(1) || current - request.CreatedUtc > TimeSpan.FromMinutes(15))
            throw new InvalidOperationException("La demande a expiré. Génère une nouvelle demande dans Atlas.");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new AdminResponse("ideo-admin-access", request.DeviceId,
            request.Nonce, name, Guid.NewGuid().ToString("N"), current.AddMinutes(5)));
        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKey);
        var signature = signer.SignData(payload, HashAlgorithmName.SHA256);
        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(publicKey ?? PublicKey);
        if (!verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            throw new CryptographicException("La clé Idéo ne correspond pas à la clé publique Atlas.");

        return $"ATLADM1.{Encode(payload)}.{Encode(signature)}";
    }

    private sealed record AdminRequest(string DeviceId, string Nonce, DateTimeOffset CreatedUtc);
    private sealed record AdminResponse(string Purpose, string DeviceId, string Nonce, string OperatorName, string TokenId, DateTimeOffset ExpiresUtc);

    private static string Encode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string data) => Convert.FromBase64String(
        data.Replace('-', '+').Replace('_', '/').PadRight((data.Length + 3) / 4 * 4, '='));

    [GeneratedRegex(@"\AATL[0-9A-F]{20}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DevicePattern();

    private sealed record Payload(string Customer, string ValidUntil, string LicenseId, string DeviceId, string[]? Features);
}

public sealed record AtlasLicenseClaims(string Customer, string LicenseId, string DeviceId, DateOnly ValidUntil, bool IsCreator);
