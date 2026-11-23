using System.Security.Cryptography;
using System.Text.Json;
using Biblideo.Core.Services;
using Microsoft.Data.Sqlite;

namespace Nexus.Service;

internal sealed record AtlasRight(string Id, Guid ClientId, string Customer, string DeviceId, DateOnly ValidUntil, bool IsCreator, long Revision);
internal sealed record AtlasWrite(Guid ClientId, string? Code, long Revision, string? Operation);
internal sealed record AtlasEvent(string Operation, string RecordedAtUtc, AtlasRight? Previous, AtlasRight Current);
internal sealed class AtlasRuleException(string message) : Exception(message);

internal sealed class AtlasRegistry(CustomerRegistry clients)
{
    public void Initialize()
    {
        using var db = clients.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS atlas_rights (
                id TEXT PRIMARY KEY NOT NULL, client_id TEXT NOT NULL REFERENCES clients(id),
                device_id TEXT UNIQUE NOT NULL, revision INTEGER NOT NULL, state TEXT NOT NULL, code TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS atlas_events (
                sequence INTEGER PRIMARY KEY, right_id TEXT NOT NULL REFERENCES atlas_rights(id),
                event TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AtlasRight> List()
    {
        using var db = clients.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT state FROM atlas_rights ORDER BY device_id";
        using var rows = query.ExecuteReader();
        var result = new List<AtlasRight>();
        while (rows.Read()) result.Add(JsonSerializer.Deserialize<AtlasRight>(rows.GetString(0))!);
        return result;
    }

    public string? ReadCode(string id)
    {
        using var db = clients.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT code FROM atlas_rights WHERE id = $id";
        query.Parameters.AddWithValue("$id", id);
        return query.ExecuteScalar() as string;
    }

    public IReadOnlyList<AtlasEvent> History(string id)
    {
        using var db = clients.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT event FROM atlas_events WHERE right_id = $id ORDER BY sequence DESC";
        query.Parameters.AddWithValue("$id", id);
        using var rows = query.ExecuteReader();
        var result = new List<AtlasEvent>();
        while (rows.Read()) result.Add(JsonSerializer.Deserialize<AtlasEvent>(rows.GetString(0))!);
        return result;
    }

    public AtlasRight Write(string id, AtlasWrite input, AtlasLicenseClaims claims, string code)
    {
        using var db = clients.Open();
        using var transaction = db.BeginTransaction();
        using var query = db.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "SELECT name FROM clients WHERE id = $client";
        query.Parameters.AddWithValue("$client", input.ClientId.ToString("D"));
        if (query.ExecuteScalar() is not string clientName || clientName != claims.Customer)
            throw new AtlasRuleException("La fiche client a changé ou ne correspond pas au bénéficiaire de la licence.");

        query.CommandText = "SELECT state FROM atlas_rights WHERE id = $id";
        query.Parameters.AddWithValue("$id", id);
        var previous = query.ExecuteScalar() is string state ? JsonSerializer.Deserialize<AtlasRight>(state) : null;
        if ((previous?.Revision ?? 0) != input.Revision)
            throw new AtlasRuleException("La licence a changé. Actualise la liste avant de continuer.");

        var right = new AtlasRight(id, input.ClientId, claims.Customer, claims.DeviceId, claims.ValidUntil,
            claims.IsCreator, input.Revision + 1);
        switch (input.Operation)
        {
            case "create" when previous is null && right.ValidUntil >= DateOnly.FromDateTime(DateTime.Today):
                break;
            case "renew" when previous is not null && right.ClientId == previous.ClientId
                             && right.DeviceId == previous.DeviceId && right.IsCreator == previous.IsCreator
                             && right.ValidUntil > previous.ValidUntil:
                break;
            case "upgrade" when previous is { IsCreator: false } && right.IsCreator && right.ClientId == previous.ClientId
                                && right.DeviceId == previous.DeviceId && right.ValidUntil == previous.ValidUntil:
                break;
            case "transfer" when previous is not null && right.ClientId == previous.ClientId
                                 && right.DeviceId != previous.DeviceId && right.IsCreator == previous.IsCreator
                                 && right.ValidUntil == previous.ValidUntil:
                break;
            default:
                throw new AtlasRuleException("Opération incompatible avec la licence actuelle ou l’échéance fournie.");
        }

        query.CommandText = """
            INSERT INTO atlas_rights (id, client_id, device_id, revision, state, code)
            VALUES ($id, $client, $device, $revision, $state, $code)
            ON CONFLICT(id) DO UPDATE SET device_id = excluded.device_id, revision = excluded.revision,
              state = excluded.state, code = excluded.code
            """;
        query.Parameters.AddWithValue("$device", right.DeviceId);
        query.Parameters.AddWithValue("$revision", right.Revision);
        query.Parameters.AddWithValue("$state", JsonSerializer.Serialize(right));
        query.Parameters.AddWithValue("$code", code);
        query.ExecuteNonQuery();

        query.Parameters.Clear();
        query.CommandText = "INSERT INTO atlas_events (right_id, event) VALUES ($id, $event)";
        query.Parameters.AddWithValue("$id", id);
        query.Parameters.AddWithValue("$event", JsonSerializer.Serialize(
            new AtlasEvent(input.Operation!, DateTimeOffset.UtcNow.ToString("O"), previous, right)));
        query.ExecuteNonQuery();
        transaction.Commit();
        return right;
    }
}

internal static class AtlasEndpoints
{
    public static void MapAtlasLicenses(this WebApplication app)
    {
        var routes = app.MapGroup("/atlas-licenses");
        routes.MapGet("", (AtlasRegistry registry) => registry.List());
        routes.MapGet("/{id}/file", (string id, AtlasRegistry registry, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return registry.ReadCode(id) is { } code ? Results.Text(code, "text/plain") : Results.NotFound();
        }).WithMetadata(NexusPermission.Administer);
        routes.MapGet("/{id}/history", (string id, AtlasRegistry registry) => registry.History(id));
        routes.MapPut("/{id}", (string id, AtlasWrite input, AtlasRegistry registry) =>
        {
            try
            {
                var code = input.Code?.Trim() ?? string.Empty;
                var claims = AtlasLicenseCodec.Read(code);
                if (claims.LicenseId != id || !Guid.TryParse(id, out _) || input.Revision < 0)
                    return Results.BadRequest(new { error = "Identifiant de licence incorrect." });

                return Results.Ok(registry.Write(id, input, claims, code));
            }
            catch (AtlasRuleException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 19)
            {
                return Results.Conflict(new { error = "Le poste est déjà associé à une licence ou le client est introuvable." });
            }
            catch (Exception error) when (error is FormatException or ArgumentException or CryptographicException or JsonException)
            {
                return Results.BadRequest(new { error = "La licence Atlas n’a pas pu être vérifiée." });
            }
        });
    }
}
