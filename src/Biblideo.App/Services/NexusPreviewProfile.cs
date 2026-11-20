using System.IO;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Services;

internal static class NexusPreviewProfile
{
#if NEXUS_PREVIEW
    public static bool Enabled => true;
#else
    public static bool Enabled => false;
#endif

    public static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions", "Nexus Preview");

    public static async Task<(AppSettings Settings, ClientModuleSettings Clients)> LoadAsync(string root)
    {
        var profileFile = Path.Combine(root, "Config", "nexus-preview.json");
        var settings = new AppSettings
        {
            SharedSettingsPath = profileFile,
            SourceScPath = Path.Combine(root, "Bibliotheques", "SC"),
            SourceEpPath = Path.Combine(root, "Bibliotheques", "EP"),
            ClientsRootPath = Path.Combine(root, "Clients"),
            IdeoSolutionsRootPath = root,
            ProductStagingPath = Path.Combine(root, "Packages"),
            ProductArchivesPath = Path.Combine(root, "Modeles"),
            ProductTrashPath = Path.Combine(root, "Corbeille", "Packages"),
            ReportsPath = Path.Combine(root, "Rapports"),
            MonthlyPackagesRootPath = Path.Combine(root, "Packs_Mensuels"),
            LicenseInboxPath = Path.Combine(root, "Licences", "Arrivee"),
            LicenseTrashPath = Path.Combine(root, "Licences", "Corbeille")
        };
        var clientSettings = new ClientModuleSettings
        {
            ArchivePath = Path.Combine(root, "Corbeille", "Clients"),
            RegistryPath = Path.Combine(root, "Data", "clients.json"),
            HistoryPath = Path.Combine(root, "Data", "clients-history.jsonl")
        };

        foreach (var directory in new[] {
            settings.ClientsRootPath, settings.ProductStagingPath, settings.ProductTrashPath,
            settings.ReportsPath, settings.MonthlyPackagesRootPath, settings.LicenseInboxPath,
            settings.LicenseTrashPath, clientSettings.ArchivePath })
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(profileFile))
        {
            SeedComponents(settings.SourceScPath);
            SeedComponents(settings.SourceEpPath);
            foreach (var variant in new[] { LibraryVariant.SC, LibraryVariant.EP })
                WriteExample(Path.Combine(settings.GetModelPath(variant), "---_Contenu_du_Pack.txt"));

            await SeedClientsAsync(ClientModuleContext.Create(settings, clientSettings));
            await new SettingsService(root).SaveAsync(settings, profileFile);
        }

        // Les chemins sont reconstruits ici, indépendamment de la configuration Biblidéo.
        return (settings, clientSettings);
    }

    private static void SeedComponents(string source)
    {
        WriteExample(Path.Combine(source, "_Ideo_Base", "Structure", "Caisson",
            "Corps_Meuble#V=Caisson#I=00#R=NR#C=Basique.top"));
        WriteExample(Path.Combine(source, "_Ideo_Base", "Agencement", "Portes",
            "Face_Meuble#V=Porte#I=01#R=NR#C=Battante.top"));
        WriteExample(Path.Combine(source, "_Ideo_Base", "Agencement", "Tiroirs",
            "Rangement_Meuble#V=Tiroir#I=02#R=NR#C=Standard.top"));
    }

    private static void WriteExample(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
            File.WriteAllText(path, "Fichier fictif pour l’aperçu Idéo Nexus. Aucun contenu TopSolid.");
    }

    private static async Task SeedClientsAsync(ClientModuleContext context)
    {
        if (File.Exists(context.ClientRegistryPath)) return;
        var registry = new ClientRegistry();
        var clientService = new ClientManagementService();
        foreach (var draft in new[] {
            new ClientDraft("Atelier des Tilleuls", ClientLibraryVariant.SC, true, ["atelier@example.invalid"]),
            new ClientDraft("Menuiserie Démo", ClientLibraryVariant.EP, false, ["menuiserie@example.invalid"]),
            new ClientDraft("Agencement Exemple", ClientLibraryVariant.SC_EP, true, ["agencement@example.invalid"]) })
        {
            var folderName = ClientFolderNamingService.BuildFolderName(draft.BaseName, draft.Variant,
                draft.IsSubscribed, context.ClientSubscriptionMarker);
            if (!Directory.Exists(Path.Combine(context.ClientsRootPath, folderName)))
                clientService.CreateClient(context, draft);
            registry.Clients.Add(new ClientMetadata
            {
                CanonicalName = ClientFolderNamingService.BuildCanonicalName(draft.BaseName),
                FolderName = folderName,
                Emails = draft.Emails.ToList()
            });
        }

        await new ClientRegistryService().SaveAsync(context.ClientRegistryPath, registry);
    }
}
