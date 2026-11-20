using System.Text.Json;

namespace Nexus.Service;

internal sealed record SnapshotDestination(string Path);

internal sealed class SnapshotExporter(
    CustomerRegistry registry,
    SnapshotDestination destination,
    ILogger<SnapshotExporter> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long? exportedRevision = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                var snapshot = registry.ReadSnapshot();
                if (snapshot.Revision == exportedRevision && File.Exists(destination.Path)) continue;
                await ExportAsync(snapshot, stoppingToken);
                exportedRevision = snapshot.Revision;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                logger.LogWarning(exception, "Export du registre Nexus indisponible. Nouvel essai au prochain cycle.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExportAsync(CustomerSnapshot snapshot, CancellationToken cancellationToken)
    {
        var temporaryPath = destination.Path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, destination.Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
