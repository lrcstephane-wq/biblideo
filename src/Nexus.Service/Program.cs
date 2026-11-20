using System.Net;
using Nexus.Service;

var builder = WebApplication.CreateSlimBuilder();
builder.Configuration.Sources.Clear();
var port = Environment.GetEnvironmentVariable("IDEO_NEXUS_DEV_PORT") is { } configuredPort
    ? int.Parse(configuredPort, System.Globalization.CultureInfo.InvariantCulture)
    : 5078;
// Cette instance de développement traite uniquement des données fictives, en local.
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));

var dataDirectory = Environment.GetEnvironmentVariable("IDEO_NEXUS_DEV_DIRECTORY")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ideo Solutions", "Nexus Development");
var directory = Path.GetFullPath(dataDirectory);
Directory.CreateDirectory(directory);

builder.Services.AddSingleton(new CustomerRegistry(Path.Combine(directory, "nexus-dev.sqlite")));
builder.Services.AddSingleton(new SnapshotDestination(Path.Combine(directory, "clients.snapshot.json")));
builder.Services.AddHostedService<SnapshotExporter>();

var app = builder.Build();
app.Services.GetRequiredService<CustomerRegistry>().Initialize();

var clients = app.MapGroup("/clients");

clients.MapGet("", (CustomerRegistry registry) => registry.ReadSnapshot());
clients.MapPost("", (CustomerName request, CustomerRegistry registry) =>
{
    if (ValidateName(request.Name) is not { } name)
        return Results.BadRequest(new { error = "Le nom doit contenir de 1 à 200 caractères." });

    var client = registry.Create(name);
    return Results.Created($"/clients/{client.Id}", client);
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
});

app.Run();

static string? ValidateName(string? value) => value?.Trim() is { Length: >= 1 and <= 200 } name ? name : null;
