using System.Net;
using Nexus.Service;

var builder = WebApplication.CreateSlimBuilder();
builder.Configuration.Sources.Clear();
var accessFile = Environment.GetEnvironmentVariable("IDEO_NEXUS_ACCESS_FILE");
var shared = accessFile is not null;
var access = shared ? SharedAccess.Load(accessFile!) : null;
var port = int.Parse(Environment.GetEnvironmentVariable(shared ? "IDEO_NEXUS_PORT" : "IDEO_NEXUS_DEV_PORT")
    ?? (shared ? "7443" : "5078"), System.Globalization.CultureInfo.InvariantCulture);
var dataDirectory = shared
    ? Required("IDEO_NEXUS_DATA_DIRECTORY")
    : Environment.GetEnvironmentVariable("IDEO_NEXUS_DEV_DIRECTORY")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ideo Solutions", "Nexus Development");
var directory = Path.GetFullPath(dataDirectory);

if (shared)
{
    var certificatePath = Required("IDEO_NEXUS_TLS_PFX");
    var certificatePassword = Environment.GetEnvironmentVariable("IDEO_NEXUS_TLS_PASSWORD");
    builder.WebHost.ConfigureKestrel(options =>
        options.ListenAnyIP(port, https => https.UseHttps(certificatePath, certificatePassword)));
}
else
{
    // L’API sans compte reste limitée à la boucle locale et à la base de développement.
    builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
}

Directory.CreateDirectory(directory);

builder.Services.AddSingleton(new CustomerRegistry(Path.Combine(directory, shared ? "nexus.sqlite" : "nexus-dev.sqlite")));
builder.Services.AddSingleton(new SnapshotDestination(Path.Combine(directory, "clients.snapshot.json")));
if (!shared) builder.Services.AddHostedService<SnapshotExporter>();
builder.Services.AddSingleton<AtlasRegistry>();

var app = builder.Build();
app.Services.GetRequiredService<CustomerRegistry>().Initialize();

app.UseRouting();
if (access is not null)
    app.Use(access.AuthorizeAsync);

app.MapGet("/session", (HttpContext context) =>
{
    var account = SharedAccess.GetAccount(context);
    return Results.Ok(new { name = account?.Name ?? "Développement local", role = account?.Role ?? "Administrator" });
});

var clients = app.MapGroup("/clients");

clients.MapGet("", (CustomerRegistry registry) => registry.ReadSnapshot());
clients.MapPost("", (CustomerName request, CustomerRegistry registry) =>
{
    if (ValidateName(request.Name) is not { } name)
        return Results.BadRequest(new { error = "Le nom doit contenir de 1 à 200 caractères." });

    var client = registry.Create(name);
    return Results.Created($"/clients/{client.Id}", client);
}).WithMetadata(NexusPermission.Edit);
clients.MapPost("/import-biblideo", (ImportedCustomer?[] records, CustomerRegistry registry) =>
{
    if (records.Length is < 1 or > 10000
        || records.Any(record => record is null || record.Id == Guid.Empty || ValidateName(record.Name) is null)
        || records.Select(record => record!.Id).Distinct().Count() != records.Length)
        return Results.BadRequest(new { error = "Le registre contient des fiches invalides ou des identifiants en double." });

    return Results.Ok(registry.Import(records.Select(record => record!).ToArray()));
});
clients.MapGet("/{id:guid}", (Guid id, CustomerRegistry registry) =>
    registry.Find(id) is { } client ? Results.Ok(client) : Results.NotFound());
clients.MapPut("/{id:guid}", (Guid id, CustomerEdit request, CustomerRegistry registry) =>
{
    if (ValidateName(request.Name) is not { } name || request.Revision < 1)
        return Results.BadRequest(new { error = "Un nom valide et la révision lue sont nécessaires." });

    var updated = registry.Rename(id, name, request.Revision);
    if (updated is not null) return Results.Ok(updated);
    return registry.Find(id) is { } current
        ? Results.Conflict(new { error = "La fiche a été modifiée. Rechargez-la avant de continuer.", current })
        : Results.NotFound();
}).WithMetadata(NexusPermission.Edit);

app.Services.GetRequiredService<AtlasRegistry>().Initialize();
app.MapAtlasLicenses();
app.Run();

static string? ValidateName(string? value) => value?.Trim() is { Length: >= 1 and <= 200 } name ? name : null;

static string Required(string variable) =>
    Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Paramètre Nexus manquant : {variable}.");
