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
