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

    File.Delete(sharedPath);
    var cached = await settingsService.LoadAsync();
    Check(settingsService.LastLoadUsedCache, "La copie locale doit être utilisée si le réseau est indisponible.");
    Check(cached.SourceScPath == settings.SourceScPath, "La copie locale doit conserver les réglages partagés.");

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
