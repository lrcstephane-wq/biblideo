using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Nexus.Service;

internal sealed record Customer(Guid Id, string Name, long Revision);
internal sealed record CustomerName(string? Name);
internal sealed record ImportedCustomer(Guid Id, string? Name);
internal sealed record CustomerImportResult(int Added, int Existing);
internal sealed record CustomerEdit(string? Name, long Revision);
internal sealed record CustomerSnapshot(int SchemaVersion, long Revision, DateTimeOffset GeneratedAtUtc, IReadOnlyList<Customer> Clients);

internal sealed class CustomerRegistry(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        ForeignKeys = true,
        DefaultTimeout = 5
    }.ToString();

    public void Initialize()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var schema = connection.CreateCommand();
        schema.Transaction = transaction;
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS clients (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK (revision >= 1)
            );
            CREATE TABLE IF NOT EXISTS client_events (
                sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                client_id TEXT NOT NULL REFERENCES clients(id),
                recorded_at_utc TEXT NOT NULL,
                previous_state TEXT,
                current_state TEXT NOT NULL
            );
            """;
        schema.ExecuteNonQuery();
        transaction.Commit();
    }

    public CustomerSnapshot ReadSnapshot()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(sequence), 0) FROM client_events";
        var revision = (long)command.ExecuteScalar()!;
        command.CommandText = "SELECT id, name, revision FROM clients ORDER BY name COLLATE NOCASE, id";
        var clients = new List<Customer>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) clients.Add(ReadCustomer(reader));
        }

        transaction.Commit();
        return new CustomerSnapshot(1, revision, DateTimeOffset.UtcNow, clients);
    }

    public Customer? Find(Guid id)
    {
        using var connection = Open();
        return Find(connection, null, id);
    }

    public Customer Create(string name)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var client = new Customer(Guid.NewGuid(), name, 1);
        Insert(connection, transaction, client);
        transaction.Commit();
        return client;
    }

    public CustomerImportResult Import(IReadOnlyList<ImportedCustomer> records)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var added = 0;
        foreach (var record in records)
        {
            if (Find(connection, transaction, record.Id) is not null) continue;
            Insert(connection, transaction, new Customer(record.Id, record.Name!.Trim(), 1));
            added++;
        }
        transaction.Commit();
        return new CustomerImportResult(added, records.Count - added);
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, Customer client)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO clients (id, name, revision) VALUES ($id, $name, 1)";
        command.Parameters.AddWithValue("$id", client.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", client.Name);
        command.ExecuteNonQuery();
        AddEvent(connection, transaction, null, client);
    }

    public Customer? Rename(Guid id, string name, long revision)
    {
        using var connection = Open();
        // Réserve l’écriture avant de lire l’état qui sera journalisé.
        using var transaction = connection.BeginTransaction();
        var current = Find(connection, transaction, id);
        if (current is null || current.Revision != revision) return null;
        if (current.Name == name) return current;

        var updated = current with { Name = name, Revision = revision + 1 };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE clients SET name = $name, revision = $revision WHERE id = $id";
        command.Parameters.AddWithValue("$name", updated.Name);
        command.Parameters.AddWithValue("$revision", updated.Revision);
        command.Parameters.AddWithValue("$id", updated.Id.ToString("D"));
        command.ExecuteNonQuery();
        AddEvent(connection, transaction, current, updated);
        transaction.Commit();
        return updated;
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static Customer? Find(SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, name, revision FROM clients WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCustomer(reader) : null;
    }

    private static Customer ReadCustomer(SqliteDataReader reader) =>
        new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2));

    private static void AddEvent(SqliteConnection connection, SqliteTransaction transaction, Customer? previous, Customer current)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO client_events (client_id, recorded_at_utc, previous_state, current_state)
            VALUES ($id, $recorded, $previous, $current)
            """;
        command.Parameters.AddWithValue("$id", current.Id.ToString("D"));
        command.Parameters.AddWithValue("$recorded", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$previous", previous is null ? DBNull.Value : JsonSerializer.Serialize(previous));
        command.Parameters.AddWithValue("$current", JsonSerializer.Serialize(current));
        command.ExecuteNonQuery();
    }
}
