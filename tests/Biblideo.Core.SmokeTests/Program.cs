using Biblideo.App.Services;
using System.Text.Json;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

var failures = new List<string>();

Check(ComponentNameParser.TryParse("Corps_Meuble#V=Caisson#I=00#R=NR#C=Basique", out var parsed), "Le nom TopSolid doit être analysé.");
Check(parsed?.Type == "Corps_Meuble", "Le type doit être Corps_Meuble.");
Check(parsed?.Variant == "Caisson", "Le composant doit être Caisson.");
Check(parsed?.Index == "00", "L’index doit être 00.");

Check(ComponentNameParser.TryParse("Corps_Meuble#V=Caisson#I=00", out var cat), "Le nom CAT doit être analysé.");
Check(parsed?.CatKey == cat?.CatKey, "La clé CAT doit ignorer R et C.");

var productName = NamingService.BuildProductPackageName(1, "Caisson Basique", LibraryVariant.SC);
Check(productName == "001_Caisson_Basique_(SC)", "Le nom produit doit respecter le format convenu.");
Check(NamingService.BuildExecutableName(productName) == "Caisson_Basique.exe", "Le nom SFX doit être dérivé du package produit.");
Check(NamingService.IsValidMonthlyName("2609_SC", LibraryVariant.SC), "2609_SC doit être valide pour SC.");
Check(!NamingService.IsValidMonthlyName("2609_EP", LibraryVariant.SC), "Une opération SC ne doit jamais accepter EP.");

var testRoot = Path.Combine(Path.GetTempPath(), "Biblideo-SmokeTests", Guid.NewGuid().ToString("N"));
var sharedPath = Path.Combine(testRoot, "shared", "biblideo.settings.json");
var localPath = Path.Combine(testRoot, "local");
try
{
    var settingsService = new SettingsService(localPath);
    var settings = new AppSettings { SourceScPath = @"C:\Test\SourceSC" };
    await settingsService.SaveAsync(settings, sharedPath);
    var loaded = await settingsService.LoadAsync();
    Check(loaded.SourceScPath == settings.SourceScPath, "La configuration partagée doit être relue.");
    Check(loaded.SharedSettingsPath == sharedPath, "Le poste doit conserver l’emplacement de la configuration partagée.");
    Check(File.Exists(settingsService.CacheFilePath), "Une copie locale de la configuration doit être créée.");
    Check(File.Exists(settingsService.MachinePathsFilePath), "Les chemins propres au poste doivent être mémorisés séparément.");

    var otherPostSettings = new AppSettings { SourceScPath = @"Z:\BibliothequeSC" };
    await new SettingsService(Path.Combine(testRoot, "other-local")).SaveAsync(otherPostSettings, sharedPath);
    loaded = await settingsService.LoadAsync();
    Check(loaded.SourceScPath == settings.SourceScPath, "Chaque poste doit conserver son propre chemin malgré une configuration partagée.");

    File.Delete(sharedPath);
    var cached = await settingsService.LoadAsync();
    Check(settingsService.LastLoadUsedCache, "La copie locale doit être utilisée si le réseau est indisponible.");
    Check(cached.SourceScPath == settings.SourceScPath, "La copie locale doit conserver les réglages partagés.");

    var previewRoot = Path.Combine(testRoot, "nexus-preview");
    var (previewSettings, previewClients) = await NexusPreviewProfile.LoadAsync(previewRoot);
    var previewRegistry = await new ClientRegistryService().LoadAsync(previewClients.RegistryPath);
    var previewContext = ClientModuleContext.Create(previewSettings, previewClients);
    var previewScan = new ClientManagementService().Scan(previewContext, previewRegistry);
    Check(previewScan.Clients.Count == 3 && previewScan.Clients.All(client => client.StructureIsValid),
        "L’aperçu doit générer trois fiches fictives avec leurs sous-dossiers.");
    Check(previewClients.NotificationRecipients.Count == 0 && previewSettings.ClientsRootPath.StartsWith(previewRoot),
        "L’aperçu doit rester isolé des clients et des destinataires habituels.");
    var sourceRegistry = await File.ReadAllTextAsync(previewClients.RegistryPath);
    var directoryClients = await BiblideoClientDirectory.ReadAsync(previewContext);
    Check(directoryClients.Select(client => client.Id).ToHashSet().SetEquals(previewRegistry.Clients.Select(client => client.Id)),
        "Le générateur Atlas doit reprendre les identifiants des dossiers Biblidéo.");
    Check(directoryClients.Any(client => client.Name == "Atelier des Tilleuls")
          && await File.ReadAllTextAsync(previewClients.RegistryPath) == sourceRegistry,
        "La lecture doit retrouver le nom sans modifier le registre source.");
    var ghost = new ClientMetadata { CanonicalName = "absent", FolderName = "Absent_(SC)" };
    previewRegistry.Clients.Add(ghost);
    await new ClientRegistryService().SaveAsync(previewClients.RegistryPath, previewRegistry);
    Check((await BiblideoClientDirectory.ReadAsync(previewContext)).All(client => client.Id != ghost.Id),
        "Un ancien identifiant sans dossier ne doit plus apparaître dans le générateur.");
    previewRegistry.Clients.Remove(ghost);
    await new ClientRegistryService().SaveAsync(previewClients.RegistryPath, previewRegistry);

    var nasRoot = Directory.CreateDirectory(Path.Combine(testRoot, "nas")).FullName;
    var cloudRoot = Directory.CreateDirectory(Path.Combine(testRoot, "cloud")).FullName;
    using var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    var key = signingKey.ExportECPrivateKeyPem();
    var publicKey = signingKey.ExportSubjectPublicKeyInfoPem();
    var nasRegistry = new NexusFileRegistry(nasRoot, readOnly: false, publicKey);
    var secondPost = new NexusFileRegistry(nasRoot, readOnly: false, publicKey);
    var cloudRegistry = new NexusFileRegistry(cloudRoot, readOnly: true, publicKey);
    var customer = directoryClients[0];
    var legacy = new NexusCustomer(Guid.NewGuid(), "Ancienne fiche Nexus");
    var id = Guid.NewGuid().ToString("N");
    var today = DateOnly.FromDateTime(DateTime.Today);
    var expiry = today.AddDays(30);
    var oldClaims = new AtlasLicenseClaims(legacy.Name, id, "ATL-0000-0000-0000-0000-0001", expiry, true);
    var oldCode = AtlasLicenseCodec.Sign(oldClaims, key, publicKey);
    var oldRight = new NexusAtlasRight(id, legacy.Id, legacy.Name, oldClaims.DeviceId, expiry, true, 1);
    string RegistryPath(string root) => Path.Combine(root, NexusFileRegistry.DirectoryName, NexusFileRegistry.RegistryName);
    Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath(nasRoot))!);
    await File.WriteAllTextAsync(RegistryPath(nasRoot), JsonSerializer.Serialize(new
    {
        schemaVersion = 1, revision = 1L, updatedAtUtc = DateTimeOffset.UtcNow, clients = new[] { legacy },
        atlas = new[] { new { right = oldRight, code = oldCode } },
        events = new[] { new { operation = "create", recordedAtUtc = DateTimeOffset.UtcNow, previous = (NexusAtlasRight?)null, current = oldRight } }
    }));
    var legacyFile = await File.ReadAllTextAsync(RegistryPath(nasRoot));
    Check((await nasRegistry.ReadAtlasAsync()).Single().ClientId == legacy.Id, "Les licences Atlas v1 doivent rester lisibles.");
    var claims = oldClaims with { Customer = customer.Name };
    string Signed(AtlasLicenseClaims value) => AtlasLicenseCodec.Sign(value, key, publicKey);
    await nasRegistry.WriteAtlasAsync(id, customer, Signed(claims), 1, "link");
    Check((await nasRegistry.ReadAtlasAsync()).Single().ClientId == customer.Id
          && (await nasRegistry.ReadHistoryAsync(id)).Length == 2
          && (await nasRegistry.ReadHistoryAsync(id))[0].Previous?.ClientId == legacy.Id,
        "Le rattachement doit reprendre l’identifiant commun et conserver le droit et l’historique.");
    Check(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(RegistryPath(nasRoot))!, "registre-nexus-v1.bak")) == legacyFile,
        "Le fichier d’origine doit être sauvegardé lors de la première écriture au nouveau format.");
    using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(RegistryPath(nasRoot))))
        Check(json.RootElement.GetProperty("schemaVersion").GetInt32() == 2 && !json.RootElement.TryGetProperty("clients", out _),
            "Le registre Atlas v2 ne doit plus contenir de liste de clients.");

    async Task<bool> TryRenewAsync(NexusFileRegistry registry, int days)
    {
        try
        {
            await registry.WriteAtlasAsync(id, customer, Signed(claims with { ValidUntil = expiry.AddDays(days) }), 2, "renew");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Chaque signature est préparée avant de lancer deux écritures concurrentes.
    var renewalA = Signed(claims with { ValidUntil = expiry.AddDays(1) });
    var renewalB = Signed(claims with { ValidUntil = expiry.AddDays(2) });
    async Task<bool> TryWriteAsync(NexusFileRegistry registry, string code)
    {
        try
        {
            await registry.WriteAtlasAsync(id, customer, code, 2, "renew");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    var attempts = await Task.WhenAll(
        Task.Run(() => TryWriteAsync(nasRegistry, renewalA)),
        Task.Run(() => TryWriteAsync(secondPost, renewalB)));
    Check(attempts.Count(result => result) == 1, "Deux renouvellements sur la même révision ne doivent pas s’écraser.");
    Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath(cloudRoot))!);
    File.Copy(RegistryPath(nasRoot), RegistryPath(cloudRoot));
    var cloudContents = await File.ReadAllTextAsync(RegistryPath(cloudRoot));
    var rights = await cloudRegistry.ReadAtlasAsync();
    var nasRights = await nasRegistry.ReadAtlasAsync();
    Check(Enumerable.SequenceEqual(rights, nasRights)
          && !await TryRenewAsync(cloudRegistry, 7)
          && await File.ReadAllTextAsync(RegistryPath(cloudRoot)) == cloudContents,
        "La copie cloud doit restituer les licences et rester en lecture seule.");
    Check(Biblideo.App.ViewModels.ClientRowViewModel.GetAtlasStatus(rights, today).Contains("1/1")
          && Biblideo.App.ViewModels.ClientRowViewModel.GetAtlasStatus(rights, expiry.AddDays(3)).Contains("expirées")
          && Biblideo.App.ViewModels.ClientRowViewModel.GetAtlasStatus([], today).Contains("Aucune")
          && Biblideo.App.ViewModels.ClientRowViewModel.GetAtlasStatus(null, today).Contains("Indisponible"),
        "La fiche client doit distinguer licence active, expirée, absente et registre indisponible.");

    async Task<bool> TryRemoveAtlasAsync(NexusFileRegistry registry, NexusAtlasRight[] selected, bool entireCompany)
    {
        try
        {
            await registry.RemoveAtlasAsync(customer.Id, selected, entireCompany);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    async Task<string> CreateAtlasPostAsync(NexusCustomer beneficiary, string device)
    {
        var newId = Guid.NewGuid().ToString("N");
        var newClaims = new AtlasLicenseClaims(beneficiary.Name, newId, device, expiry, false);
        await nasRegistry.WriteAtlasAsync(newId, beneficiary, Signed(newClaims), 0, "create");
        return newId;
    }

    var secondId = await CreateAtlasPostAsync(customer, "ATL-0000-0000-0000-0000-0002");
    var otherId = await CreateAtlasPostAsync(directoryClients[1], "ATL-0000-0000-0000-0000-0003");
    Check(!await TryRemoveAtlasAsync(nasRegistry, nasRights, entireCompany: true)
          && (await nasRegistry.ReadAtlasAsync()).Length == 3,
        "Une suppression groupée doit refuser une sélection périmée sans supprimer de licence.");
    Check(!await TryRemoveAtlasAsync(cloudRegistry, rights, entireCompany: false)
          && await File.ReadAllTextAsync(RegistryPath(cloudRoot)) == cloudContents,
        "La liste cloud interdit aussi les suppressions de licences.");
    var displayed = await nasRegistry.ReadAtlasAsync();
    var single = displayed.Single(right => right.Id == secondId);
    Check(await TryRemoveAtlasAsync(nasRegistry, [single], entireCompany: false)
          && (await nasRegistry.ReadAtlasAsync()).Any(right => right.Id == id)
          && (await nasRegistry.ReadHistoryAsync(secondId)).First().Operation == "remove",
        "La suppression d’un poste laisse les autres licences du client et conserve l’événement.");
    var remainingRights = await nasRegistry.ReadAtlasAsync();
    var companyRights = remainingRights.Where(right => right.ClientId == customer.Id).ToArray();
    Check(await TryRemoveAtlasAsync(nasRegistry, companyRights, entireCompany: true)
          && (await nasRegistry.ReadAtlasAsync()).Single().Id == otherId
          && (await nasRegistry.ReadHistoryAsync(id)).First().Operation == "remove",
        "La suppression groupée conserve les licences des autres sociétés et journalise l’opération.");

    var requestTime = DateTimeOffset.UtcNow;
    var requestJson = JsonSerializer.SerializeToUtf8Bytes(new
    {
        DeviceId = oldClaims.DeviceId, Nonce = Guid.NewGuid().ToString("N"), CreatedUtc = requestTime
    });
    var requestCode = "ATLREQ1." + Convert.ToBase64String(requestJson).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var adminCode = AtlasLicenseCodec.SignAdminResponse(requestCode, "Opérateur test", key, requestTime, publicKey);
    Check(adminCode.StartsWith("ATLADM1.", StringComparison.Ordinal) && adminCode.Split('.').Length == 3,
        "Le générateur doit restituer une réponse signée au format administrateur Atlas.");
    try
    {
        AtlasLicenseCodec.SignAdminResponse(requestCode, "Opérateur test", key, requestTime.AddMinutes(16), publicKey);
        Check(false, "Une demande Administrateur Atlas expirée doit être refusée.");
    }
    catch (InvalidOperationException)
    {
    }

    var demoScan = await new LibraryScanner().ScanAsync(previewSettings.SourceScPath);
    var demoPlan = new ProductPackageSimulationService().BuildPlan(
        demoScan, demoScan.Components, previewSettings, LibraryVariant.SC, "001_Demo_(SC)");
    Check(demoScan.Components.Count == 3 && demoPlan.Files.Count >= 4 && demoPlan.BlockingErrors.Count == 0,
        "L’aperçu doit fournir des composants avec lesquels simuler un package produit.");
    await File.WriteAllTextAsync(Path.Combine(previewRoot, "bootstrap.json"),
        JsonSerializer.Serialize(new { SharedSettingsPath = sharedPath }));
    var (reopenedPreview, reopenedPreviewClients) = await NexusPreviewProfile.LoadAsync(previewRoot);
    var reopenedRegistry = await new ClientRegistryService().LoadAsync(reopenedPreviewClients.RegistryPath);
    Check(reopenedPreview.SharedSettingsPath == previewSettings.SharedSettingsPath
          && reopenedRegistry.Clients.Select(client => client.Id).SequenceEqual(previewRegistry.Clients.Select(client => client.Id)),
        "L’aperçu doit ignorer un pointeur externe et conserver les identifiants de démonstration entre deux lancements.");

    var libraryRoot = Path.Combine(testRoot, "library");
    var componentRoot = Path.Combine(libraryRoot, "_Ideo_Utilisateur", "Caisson");
    Directory.CreateDirectory(componentRoot);
    var topPath = Path.Combine(componentRoot, "Corps_Meuble#V=Caisson#I=00#R=NR#C=Basique.top");
    File.WriteAllText(topPath, "top");
    File.WriteAllText(topPath + ".png", "png");
    File.WriteAllText(Path.Combine(componentRoot, "Corps_Meuble#V=Caisson#I=00.cat"), "cat");
    File.WriteAllText(Path.Combine(componentRoot, "temp.bak"), "bak");
    File.WriteAllText(Path.Combine(componentRoot, "lib.cfg"), "cfg");
    File.WriteAllText(Path.Combine(componentRoot, "inattendu"), "sans extension");

    var scan = await new LibraryScanner().ScanAsync(libraryRoot);
    Check(scan.Components.Count == 1, "Le scanner doit trouver le composant .top de test.");
    Check(scan.Components[0].AssociatedFiles.Count == 2, "Le PNG et le CAT associés doivent être détectés.");
    Check(scan.IgnoredBakCount == 1 && scan.IgnoredCfgCount == 1, "Les fichiers .bak et .cfg doivent être ignorés.");
    Check(scan.ExtensionlessCount == 1 && scan.Warnings.Count > 0, "Un fichier sans extension doit être signalé sans bloquer l’analyse.");

    var archivesRoot = Path.Combine(testRoot, "archives");
    var modelRoot = Path.Combine(archivesRoot, "000_Dossier_Produits_(SC)");
    var modelLibraryFolder = Path.Combine(modelRoot, "Bibliotheque", "_Ideo_Base", "Coulissant");
    Directory.CreateDirectory(modelLibraryFolder);
    File.WriteAllText(Path.Combine(modelLibraryFolder, "icn_Coulissant.bmp"), "icone modele");
    File.WriteAllText(Path.Combine(modelRoot, "---_Contenu_du_Pack.txt"), "document modele");
    File.WriteAllText(Path.Combine(modelRoot, "Guide_Installation_Biblideo.pdf"), "guide modele");

    var simulationSettings = new AppSettings
    {
        ProductArchivesPath = archivesRoot,
        ProductStagingPath = Path.Combine(testRoot, "staging")
    };
    var plan = new ProductPackageSimulationService().BuildPlan(
        scan,
        scan.Components,
        simulationSettings,
        LibraryVariant.SC,
        productName);
    Check(plan.Files.Any(file => file.RelativeDestination == Path.Combine("Bibliotheque", "_Ideo_Utilisateur", "Caisson", Path.GetFileName(topPath))),
        "Le .top doit conserver l’architecture relative de la bibliothèque source.");
    Check(plan.Files.Any(file => file.RelativeDestination == Path.Combine("Bibliotheque", "_Ideo_Utilisateur", "Caisson", Path.GetFileName(topPath) + ".png")),
        "Le PNG associé doit être placé à côté du .top.");
    Check(plan.Files.Any(file => file.RelativeDestination == "001_Contenu_du_Pack.txt"),
        "Le compteur --- du modèle doit être remplacé dans le plan.");
    Check(plan.Files.Any(file => file.RelativeDestination == "Guide_Installation_Biblideo.pdf"),
        "Les documents fixes du modèle doivent être repris.");
    Check(plan.Directories.Any(directory => directory.RelativeDestination == Path.Combine("Bibliotheque", "_Ideo_Utilisateur", "Caisson")),
        "Tous les dossiers parents des composants doivent être planifiés.");

    var execution = await new ProductPackageExecutionService().ExecuteAsync(plan);
    Check(Directory.Exists(plan.TargetRoot), "Le service d’exécution doit créer le dossier du package.");
    Check(execution.Files.Count == plan.Files.Count, "Tous les fichiers planifiés doivent être copiés et contrôlés.");
    Check(File.Exists(execution.ManifestPath), "Un manifeste de traçabilité doit être écrit dans le package.");
    var copiedTop = Path.Combine(plan.TargetRoot, "Bibliotheque", "_Ideo_Utilisateur", "Caisson", Path.GetFileName(topPath));
    Check(File.Exists(copiedTop) && File.ReadAllText(copiedTop) == "top", "Le .top doit être copié dans son architecture d’origine.");
    Check(File.Exists(topPath), "Le fichier source doit toujours exister après la copie.");

    var secondExecutionWasBlocked = false;
    try
    {
        await new ProductPackageExecutionService().ExecuteAsync(plan);
    }
    catch (IOException)
    {
        secondExecutionWasBlocked = true;
    }
    Check(secondExecutionWasBlocked, "Une seconde exécution doit être refusée afin de ne jamais écraser la destination.");

    var secondTopPath = Path.Combine(componentRoot, "Corps_Meuble#V=Dossier_Interieur#I=00#R=NR.top");
    File.WriteAllText(secondTopPath, "second-top");
    File.WriteAllText(secondTopPath + ".png", "second-png");
    var updatedScan = await new LibraryScanner().ScanAsync(libraryRoot);
    var existingService = new ExistingProductPackageService();
    var catalog = existingService.Scan(simulationSettings.ProductStagingPath);
    Check(catalog.Packages.Count == 1, "Le package créé doit être retrouvé dans la liste des packages à déployer.");
    var existingPackage = catalog.Packages.Single();
    Check(existingPackage.ComponentCount == 1, "Le contenu .top du package doit être reconstruit.");

    var modificationSettings = new AppSettings
    {
        ProductArchivesPath = archivesRoot,
        ProductStagingPath = simulationSettings.ProductStagingPath,
        ProductTrashPath = Path.Combine(testRoot, "trash")
    };
    var secondComponent = updatedScan.Components.Single(component => component.TopFilePath == secondTopPath);
    var modificationService = new ProductPackageModificationService();
    var modificationPlan = modificationService.BuildPlan(
        existingPackage,
        updatedScan,
        [secondComponent],
        modificationSettings,
        ExistingFileCopyMode.Overwrite);
    Check(modificationPlan.AddedComponents.Count == 1 && modificationPlan.RemovedComponents.Count == 1,
        "La comparaison doit distinguer le composant ajouté et celui retiré.");
    var modificationResult = await modificationService.ExecuteAsync(modificationPlan, modificationSettings);
    Check(File.Exists(secondTopPath), "La modification ne doit jamais retirer le nouveau composant de la bibliothèque source.");
    Check(File.Exists(topPath), "La modification ne doit jamais retirer l’ancien composant de la bibliothèque source.");
    Check(File.Exists(Path.Combine(existingPackage.FullPath, "Bibliotheque", secondComponent.RelativeTopPath)),
        "Le composant ajouté doit être copié à son emplacement relatif dans le package.");
    Check(!File.Exists(copiedTop), "Le composant décoché doit quitter uniquement le package produit.");
    Check(File.Exists(Path.Combine(modificationResult.RecoveryPath, "Bibliotheque", scan.Components[0].RelativeTopPath)),
        "Le composant retiré doit rester récupérable dans la sauvegarde de modification.");

    var refreshedPackage = existingService.Scan(simulationSettings.ProductStagingPath).Packages.Single();
    var trashResult = await existingService.MoveToTrashAsync(
        refreshedPackage,
        simulationSettings.ProductStagingPath,
        modificationSettings.ProductTrashPath);
    Check(!Directory.Exists(refreshedPackage.FullPath) && Directory.Exists(trashResult.TrashPath),
        "La suppression doit déplacer le package complet dans la corbeille récupérable.");
    Check(File.Exists(secondTopPath), "La mise en corbeille du package ne doit pas toucher à la bibliothèque source.");

    var clientsRoot = Path.Combine(testRoot, "clients");
    var clientDataRoot = Path.Combine(testRoot, "client-data");
    var clientAppSettings = new AppSettings
    {
        ClientsRootPath = clientsRoot
    };
    var clientModuleSettings = new ClientModuleSettings
    {
        ArchivePath = Path.Combine(testRoot, "client-archives"),
        RegistryPath = Path.Combine(clientDataRoot, "clients.json"),
        HistoryPath = Path.Combine(clientDataRoot, "history.jsonl")
    };
    var clientSettings = ClientModuleContext.Create(clientAppSettings, clientModuleSettings);
    Directory.CreateDirectory(clientsRoot);
    var clientManagement = new ClientManagementService();
    var clientRegistryService = new ClientRegistryService();
    var clientDraft = new ClientDraft("Client Démo", ClientLibraryVariant.EP, true, ["client@example.invalid"]);
    var clientPath = clientManagement.CreateClient(clientSettings, clientDraft);
    Check(Path.GetFileName(clientPath) == "_Client_Démo_(EP)", "Le dossier client doit respecter le format officiel.");
    Check(clientSettings.ClientFolderStructure.All(folder => Directory.Exists(Path.Combine(clientPath, folder))), "La structure client complète doit être créée.");

    var clientRegistry = new ClientRegistry();
    clientRegistry.Clients.Add(new ClientMetadata
    {
        CanonicalName = ClientFolderNamingService.BuildCanonicalName(clientDraft.BaseName),
        FolderName = Path.GetFileName(clientPath),
        Emails = clientDraft.Emails.ToList()
    });
    var historyPath = Path.Combine(testRoot, "history", "clients.jsonl");
    Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
    var oldEvent = new ClientHistoryEntry { Action = "create", ClientName = "Client Démo" };
    var oldOptions = new JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(historyPath, JsonSerializer.Serialize(oldEvent, oldOptions) + Environment.NewLine);

    var newEvent = new ClientHistoryEntry { Action = "edit", ClientName = "Client Démo", Details = "Texte\nmultiligne" };
    await clientRegistryService.AppendHistoryAsync(historyPath, newEvent);
    var history = OperationHistory.ReadEntries(historyPath)
        .Select(entry => JsonSerializer.Deserialize<ClientHistoryEntry>(entry))
        .ToArray();
    Check(history.Length == 2 && history[0]?.Action == "create" && history[1]?.Details == newEvent.Details,
        "Le journal doit relire les anciens événements indentés et les nouveaux événements JSONL.");
    Check(File.ReadLines(historyPath).Last().StartsWith('{'),
        "Un événement ajouté au journal doit tenir sur une ligne.");
    await clientRegistryService.SaveAsync(clientSettings.ClientRegistryPath, clientRegistry);
    var clientBiblideoPath = Path.Combine(clientPath, clientSettings.ClientBiblideoFolderName);
    Directory.CreateDirectory(Path.Combine(clientBiblideoPath, "2607"));
    File.WriteAllText(Path.Combine(clientBiblideoPath, "2608_EP.exe"), "pack");
    var clientScan = clientManagement.Scan(clientSettings, await clientRegistryService.LoadAsync(clientSettings.ClientRegistryPath));
    Check(clientScan.Clients.Count == 1, "Le scanner clients doit reconnaître le dossier créé.");
    var scannedClient = clientScan.Clients.Single();
    Check(scannedClient.IsSubscribed && scannedClient.Variant == ClientLibraryVariant.EP, "Le préfixe et la version du client doivent être analysés.");
    Check(scannedClient.LatestDeploymentDisplay == "2608", "Le dernier pack EP doit être détecté avant l’ancien format aamm.");
    Check(scannedClient.Emails.Single() == "client@example.invalid", "Les mails doivent provenir du registre partagé.");

    Directory.CreateDirectory(Path.Combine(clientBiblideoPath, "2609_EP"));
    Directory.CreateDirectory(Path.Combine(clientBiblideoPath, "2610_SC_EP"));
    Directory.CreateDirectory(Path.Combine(clientBiblideoPath, "2611_SC"));
    Directory.CreateDirectory(Path.Combine(clientBiblideoPath, "2619_EP"));
    var latestEp = clientManagement.Scan(clientSettings, clientRegistry).Clients.Single();
    Check(latestEp.LatestDeploymentDisplay == "2610",
        "Le pack SC_EP doit compter pour EP sans retenir un pack SC ni un mois invalide.");
    var scPath = clientManagement.CreateClient(clientSettings,
        new ClientDraft("Client SC", ClientLibraryVariant.SC, true, []));
    var scPackages = Path.Combine(scPath, clientSettings.ClientBiblideoFolderName);
    Directory.CreateDirectory(Path.Combine(scPackages, "2609_SC"));
    Directory.CreateDirectory(Path.Combine(scPackages, "2610_SC_EP"));
    Directory.CreateDirectory(Path.Combine(scPackages, "2611_EP"));
    Check(clientManagement.Scan(clientSettings, clientRegistry).Clients.Single(client => client.Variant == ClientLibraryVariant.SC)
              .LatestDeploymentDisplay == "2610",
        "Avec la structure du NAS, 2610_SC_EP doit remplacer 2609_SC pour un client SC.");
    Directory.Delete(scPath, recursive: true);

    var statusChange = new ClientSubscriptionChange(scannedClient, false);
    var renamed = await clientManagement.ApplySubscriptionChangesAsync(clientSettings, [statusChange]);
    Check(renamed.Count == 1 && Directory.Exists(Path.Combine(clientsRoot, "Client_Démo_(EP)")), "La désactivation doit uniquement retirer le marqueur du dossier.");
    Check(!Directory.Exists(clientPath), "L’ancien nom marqué ne doit plus exister après le renommage.");

    var unmarkedClient = new ClientRecord
    {
        MetadataId = scannedClient.MetadataId,
        BaseName = scannedClient.BaseName,
        DisplayName = scannedClient.DisplayName,
        FolderName = "Client_Démo_(EP)",
        FullPath = Path.Combine(clientsRoot, "Client_Démo_(EP)"),
        Variant = ClientLibraryVariant.EP,
        IsSubscribed = false,
        Emails = scannedClient.Emails
    };
    Directory.Delete(Path.Combine(unmarkedClient.FullPath, "90_Dev_Spe"));
    Check(clientManagement.RepairStructure(clientSettings, unmarkedClient).Count == 1, "La réparation doit recréer uniquement le sous-dossier manquant.");
    var inventory = clientManagement.InspectFolder(clientSettings, unmarkedClient);
    Check(!inventory.IsEmpty && inventory.FileCount == 1, "L’inventaire doit compter le pack mensuel présent.");
    var archivedClientPath = clientManagement.ArchiveClient(clientSettings, unmarkedClient);
    Check(Directory.Exists(archivedClientPath) && !Directory.Exists(unmarkedClient.FullPath), "L’archivage doit déplacer le client vers le dossier récupérable.");
    var archivedMetadata = clientRegistry.Clients.Single();
    archivedMetadata.IsArchived = true;
    archivedMetadata.ArchivedPath = archivedClientPath;
    archivedMetadata.FolderName = unmarkedClient.FolderName;
    var restoredPath = clientManagement.RestoreArchivedClient(clientSettings, archivedMetadata);
    Check(Directory.Exists(restoredPath) && !Directory.Exists(archivedClientPath), "Une archive client doit pouvoir être restaurée.");

    Check(ClientFolderNamingService.TryParse("_Client_Test_(EP+SC)", "_", out var legacyDual) && legacyDual?.Variant == ClientLibraryVariant.SC_EP,
        "L’ordre historique EP+SC doit rester reconnu.");

    var emptyDraft = new ClientDraft("Client Vide", ClientLibraryVariant.SC, false, ["empty@example.invalid"]);
    var emptyPath = clientManagement.CreateClient(clientSettings, emptyDraft);
    var emptyClient = new ClientRecord
    {
        MetadataId = Guid.NewGuid(), BaseName = "Client_Vide", DisplayName = "Client Vide", FolderName = Path.GetFileName(emptyPath),
        FullPath = emptyPath, Variant = ClientLibraryVariant.SC, IsSubscribed = false
    };
    Check(clientManagement.InspectFolder(clientSettings, emptyClient).IsEmpty, "Un client ne contenant que des dossiers doit être considéré comme vide.");
    clientManagement.PermanentlyDeleteEmptyClient(clientSettings, emptyClient);
    Check(!Directory.Exists(emptyPath), "La suppression définitive doit être possible uniquement pour un dossier client vide.");

    var licenseService = new LicenseManagementService();
    Check(LicenseManagementService.ParseExpiryDate("version=xx, module=2, key=abc, date=07/09/2026") == new DateTime(2026, 9, 7),
        "La date de fin TopSolid doit être lue dans le corps du mail.");
    var licenseClient = new ClientRecord
    {
        MetadataId = Guid.NewGuid(), BaseName = "Client_Licence", DisplayName = "Client Licence", FolderName = "_Client_Licence_(EP)",
        FullPath = restoredPath, Variant = ClientLibraryVariant.EP, IsSubscribed = true,
        Emails = ["licence@example.invalid"]
    };
    var incoming = new ParsedLicenseMessage
    {
        FullPath = Path.Combine(testRoot, "license.msg"), FileName = "license.msg",
        Recipients = ["LICENCE@EXAMPLE.INVALID"], ExpiryDate = new DateTime(2026, 9, 7)
    };
    var match = licenseService.Match(incoming, [licenseClient]);
    Check(match.MatchStatus == LicenseMatchStatus.Matched && match.MatchedClient?.MetadataId == licenseClient.MetadataId,
        "Une adresse destinataire doit retrouver son client sans tenir compte de la casse.");
    var summary = licenseService.Summarize(licenseClient,
    [
        incoming,
        new ParsedLicenseMessage { FullPath = "today.msg", FileName = "today.msg", ExpiryDate = new DateTime(2026, 8, 23) },
        new ParsedLicenseMessage { FullPath = "old.msg", FileName = "old.msg", ExpiryDate = new DateTime(2026, 8, 22) },
        new ParsedLicenseMessage { FullPath = "unknown.msg", FileName = "unknown.msg" },
        new ParsedLicenseMessage { FullPath = "error.msg", FileName = "error.msg", ExpiryDate = new DateTime(2026, 9, 6), Error = "Lecture incomplète" }
    ], new DateTime(2026, 8, 23, 16, 0, 0), 10);
    Check(summary.ValidLicenseCount == 2 && summary.MessageCount == 5 && summary.UnreadableCount == 2,
        "Le compteur doit inclure le jour d’échéance et exclure les licences expirées ou illisibles.");
    Check(licenseService.Summarize(emptyClient, [incoming], new DateTime(2026, 8, 23), 10) is
        { ValidLicenseCount: 1, State: ClientLicenseState.OffSubscription },
        "Une licence valide doit rester comptée hors abonnement aux mises à jour.");
    Check(licenseService.Summarize(licenseClient, [], new DateTime(2026, 8, 23), 10).ValidLicenseCount == 0,
        "Un client sans archive doit afficher zéro licence valide.");
    Check(summary.ExpiryDate == new DateTime(2026, 9, 7) && summary.State == ClientLicenseState.Valid,
        "Le suivi doit conserver l’échéance la plus éloignée parmi plusieurs licences.");
    File.WriteAllText(incoming.FullPath, "contenu msg de test");
    var licenseDestination = Path.Combine(restoredPath, "01_Licence_Biblideo");
    var move = licenseService.MoveVerified(incoming.FullPath, licenseDestination);
    Check(move.Success && !File.Exists(incoming.FullPath) && move.DestinationPath is not null && File.Exists(move.DestinationPath),
        "La distribution doit copier, contrôler puis retirer uniquement le mail de la boîte d’arrivée.");
}
finally
{
    if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures.Select(failure => "ECHEC: " + failure)));
    return 1;
}

Console.WriteLine("Contrôles métier essentiels réussis.");
return 0;

void Check(bool condition, string message)
{
    if (!condition) failures.Add(message);
}
