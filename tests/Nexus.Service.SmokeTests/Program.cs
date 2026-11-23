using System.Security.Cryptography;
using Biblideo.Core.Services;
using Microsoft.Data.Sqlite;
using Nexus.Service;

var directory = Directory.CreateTempSubdirectory("nexus-atlas-test-");
try
{
    var customers = new CustomerRegistry(Path.Combine(directory.FullName, "test.sqlite"));
    customers.Initialize();
    var customer = customers.Create("Idéo Essai");
    var atlas = new AtlasRegistry(customers);
    atlas.Initialize();

    using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicKey = signingKey.ExportSubjectPublicKeyInfoPem();
    var id = Guid.NewGuid().ToString("N");
    var expiry = DateOnly.FromDateTime(DateTime.Today.AddDays(20));
    var first = new AtlasLicenseClaims(customer.Name, id, "ATL-0000-0000-0000-0000-0001", expiry, false);

    AtlasRight Store(AtlasLicenseClaims claims, long revision, string operation)
    {
        var code = AtlasLicenseCodec.Sign(claims, signingKey.ExportECPrivateKeyPem(), publicKey);
        var verified = AtlasLicenseCodec.Read(code, publicKey);
        Require(verified == claims, "Les métadonnées signées doivent être conservées.");
        RequireThrows<CryptographicException>(() => AtlasLicenseCodec.Read(code));
        return atlas.Write(claims.LicenseId, new(customer.Id, code, revision, operation), verified, code);
    }

    var created = Store(first, 0, "create");
    Require(!created.IsCreator && created.Revision == 1, "Création Standard incorrecte.");
    RequireThrows<SqliteException>(() => Store(first with { LicenseId = Guid.NewGuid().ToString("N") }, 0, "create"));
    var upgraded = Store(first with { IsCreator = true }, 1, "upgrade");
    Require(upgraded.IsCreator && upgraded.ValidUntil == expiry, "Le passage en Créateur doit préserver la date.");
    RequireThrows<AtlasRuleException>(() => Store(first with { IsCreator = true, ValidUntil = expiry.AddDays(30) }, 1, "renew"));
    var renewed = Store(first with { IsCreator = true, ValidUntil = expiry.AddDays(30) }, 2, "renew");
    var replacement = first with { IsCreator = true, ValidUntil = renewed.ValidUntil, DeviceId = "ATL-0000-0000-0000-0000-0002" };
    var moved = Store(replacement, 3, "transfer");
    Require(moved.Id == id && moved.ValidUntil == renewed.ValidUntil && moved.DeviceId == replacement.DeviceId,
        "Le transfert doit conserver le droit et l’échéance.");
    var history = atlas.History(id);
    Require(history.Count == 4 && history[0].Previous?.DeviceId == first.DeviceId,
        "L’historique doit conserver le poste remplacé et les opérations réussies uniquement.");
    Require(AtlasLicenseCodec.Read(atlas.ReadCode(id)!, publicKey) == replacement, "Le fichier exportable doit être celui de la licence courante.");
    Require(atlas.List().Single() == moved, "Le registre doit restituer la dernière licence.");
    Console.WriteLine("Atlas. Signature, Standard, Créateur, renouvellement, transfert, conflit, historique et export vérifiés.");
}
finally
{
    SqliteConnection.ClearAllPools();
    directory.Delete(recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RequireThrows<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Exception attendue {typeof(T).Name}.");
}
