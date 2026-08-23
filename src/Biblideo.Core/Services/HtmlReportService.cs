using System.Net;
using System.Text;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class HtmlReportService
{
    // CSS volontairement compatible avec le moteur WebBrowser Windows (IE), sans variables CSS ni Grid.
    private const string ModernReportStyle = """
        *{box-sizing:border-box}html,body{min-height:100%}body{margin:0;background:#0b1320;color:#f3f7fc;font:14px 'Segoe UI',Arial,sans-serif}main{max-width:1380px;margin:0 auto;padding:34px}.hero{background:#173c69;border:1px solid #40789d;border-radius:18px;padding:30px}.eyebrow{color:#8abcf5;text-transform:uppercase;letter-spacing:2px;font-size:11px;font-weight:bold}h1{margin:8px 0 5px;font-size:32px;color:#fff}h2{color:#fff}.muted{color:#9fb0c4}.quip{margin-top:17px;color:#b9d4ef;font-style:italic}.grid{display:table;width:100%;table-layout:fixed;border-spacing:12px;margin:8px -12px}.card{display:table-cell;width:25%;padding:18px}.card,.panel,details{background:#162232;border:1px solid #344961;border-radius:13px}.value{font-size:25px;font-weight:bold;margin-top:7px;color:#f3f7fc}.panel{padding:20px;margin-top:15px}details{display:block;margin-top:12px;overflow:hidden}summary{display:block;cursor:pointer;padding:16px 19px;font-size:16px;font-weight:bold;background:#1d2c3e;color:#fff}.detail-content{display:block;padding:8px 18px 17px}.pill{display:inline-block;padding:5px 9px;border-radius:12px;background:#25405f;color:#bcd9fa;font-size:11px;font-weight:bold;margin:2px 5px 2px 0}.ok{color:#58cf9f}.warn{color:#e2a65e}.danger{color:#ee7d8e}table{width:100%;border-collapse:collapse;margin-top:8px}th{text-align:left;color:#8abcf5;padding:10px;border-bottom:1px solid #344961}td{padding:9px 10px;border-bottom:1px solid #26384d;vertical-align:top;color:#e8eef6}code{font-family:Consolas,'Courier New',monospace;color:#b9dcff;word-wrap:break-word}.notice{padding:14px 17px;border-left:4px solid #58cf9f;background:#123b32;border-radius:9px;margin-top:16px;color:#f3f7fc}.notice.warn{border-left-color:#e2a65e;background:#503518;color:#f3f7fc}ul{line-height:1.6;padding-left:24px}@media(max-width:850px){main{padding:18px}.grid,.card{display:block;width:100%;margin:8px 0}}
        """;

    public string BuildProductPackageReport(SimulationPlan plan)
    {
        var status = plan.BlockingErrors.Count == 0 ? "Simulation prête" : "Simulation bloquée";
        var statusClass = plan.BlockingErrors.Count == 0 ? "ok" : "error";
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Rapport Biblidéo</title>")
            .Append("<style>")
            .Append("html{scrollbar-color:#66717e #23282f;scrollbar-width:thin}::-webkit-scrollbar{width:12px;height:12px}::-webkit-scrollbar-track{background:#23282f}::-webkit-scrollbar-thumb{background:#66717e;border:2px solid #23282f;border-radius:8px}::-webkit-scrollbar-thumb:hover{background:#5790dc}body{margin:0;background:#07101f;color:#dbeafe;font:14px 'Segoe UI',Arial,sans-serif}main{max-width:1280px;margin:auto;padding:32px}.hero{background:linear-gradient(135deg,#10284a,#0b5f79);border:1px solid #1c7892;border-radius:18px;padding:28px;box-shadow:0 18px 50px #0005}.eyebrow{color:#67e8f9;text-transform:uppercase;letter-spacing:.14em;font-weight:700;font-size:12px}h1{margin:8px 0 4px;font-size:30px}.muted{color:#94a3b8}.badge{display:inline-block;margin-top:15px;padding:7px 12px;border-radius:999px;font-weight:700}.ok{background:#064e3b;color:#a7f3d0}.error{background:#7f1d1d;color:#fecaca}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin:20px 0}.card{background:#0e1b30;border:1px solid #203451;border-radius:14px;padding:18px}.value{font-size:22px;font-weight:700;color:#f8fafc;margin-top:6px}.panel{background:#0e1b30;border:1px solid #203451;border-radius:14px;padding:20px;margin-top:16px}table{width:100%;border-collapse:collapse;margin-top:12px}th{text-align:left;color:#67e8f9;border-bottom:1px solid #27405f;padding:10px}td{border-bottom:1px solid #172a43;padding:9px;vertical-align:top}.warn{color:#fde68a}.danger{color:#fca5a5}.notice{border-left:4px solid #f59e0b;padding:12px 16px;background:#42200666;border-radius:8px;margin:18px 0}code{color:#bae6fd;word-break:break-all}@media(max-width:800px){.grid{grid-template-columns:1fr 1fr}}</style></head><body><main>")
            .Append("<section class=\"hero\"><div class=\"eyebrow\">Biblidéo · Rapport de contrôle</div><h1>")
            .Append(E(plan.OperationName)).Append("</h1><div class=\"muted\">Généré le ")
            .Append(E(DateTimeOffset.Now.ToString("dd/MM/yyyy à HH:mm:ss"))).Append("</div><div class=\"badge ")
            .Append(statusClass).Append("\">").Append(status).Append("</div></section>")
            .Append("<div class=\"notice\"><strong>Mode simulation stricte :</strong> aucune création, copie, modification, suppression, compression ou déplacement n’a été effectué.</div>")
            .Append("<section class=\"grid\">")
            .Append(Card("Variante", plan.Variant.ToString()))
            .Append(Card("Dossiers prévus", plan.Directories.Count.ToString("N0")))
            .Append(Card("Fichiers prévus", plan.Files.Count.ToString("N0")))
            .Append(Card("Taille estimée", FormatBytes(plan.TotalBytes)))
            .Append("</section>")
            .Append("<section class=\"panel\"><h2>Chemins contrôlés</h2><p><b>Source :</b> <code>")
            .Append(E(plan.SourceRoot)).Append("</code></p><p><b>Destination simulée :</b> <code>")
            .Append(E(plan.TargetRoot)).Append("</code></p></section>");

        AppendMessages(builder, "Erreurs bloquantes", plan.BlockingErrors, "danger");
        AppendMessages(builder, "Avertissements", plan.Warnings, "warn");

        builder.Append("<section class=\"panel\"><h2>Arborescence qui aurait été créée</h2><table><thead><tr><th>Dossier relatif</th><th>Motif</th></tr></thead><tbody>");
        foreach (var directory in plan.Directories)
        {
            builder.Append("<tr><td><code>").Append(E(directory.RelativeDestination)).Append("</code></td><td>")
                .Append(E(directory.Reason)).Append("</td></tr>");
        }
        builder.Append("</tbody></table></section>");

        builder.Append("<section class=\"panel\"><h2>Fichiers qui auraient été copiés</h2><table><thead><tr><th>Destination relative</th><th>Motif</th><th>Source</th></tr></thead><tbody>");
        foreach (var file in plan.Files)
        {
            builder.Append("<tr><td><code>").Append(E(file.RelativeDestination)).Append("</code></td><td>")
                .Append(E(file.Reason)).Append("</td><td><code>").Append(E(file.SourcePath)).Append("</code></td></tr>");
        }
        builder.Append("</tbody></table></section></main></body></html>");
        return builder.ToString();
    }

    public string BuildProductPackageExecutionReport(SimulationPlan plan, ProductPackageExecutionResult result)
    {
        var duration = result.CompletedAt - result.StartedAt;
        var builder = new StringBuilder();
        builder.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Rapport de copie Biblidéo</title>")
            .Append("<style>")
            .Append("html{scrollbar-color:#66717e #23282f;scrollbar-width:thin}::-webkit-scrollbar{width:12px;height:12px}::-webkit-scrollbar-track{background:#23282f}::-webkit-scrollbar-thumb{background:#66717e;border:2px solid #23282f;border-radius:8px}body{margin:0;background:#07101f;color:#dbeafe;font:14px 'Segoe UI',Arial,sans-serif}main{max-width:1280px;margin:auto;padding:32px}.hero{background:linear-gradient(135deg,#12345d,#17614f);border:1px solid #29836d;border-radius:18px;padding:28px;box-shadow:0 18px 50px #0005}.eyebrow{color:#79e4c1;text-transform:uppercase;letter-spacing:.14em;font-weight:700;font-size:12px}h1{margin:8px 0 4px;font-size:30px}.muted{color:#94a3b8}.badge{display:inline-block;margin-top:15px;padding:7px 12px;border-radius:999px;font-weight:700;background:#064e3b;color:#a7f3d0}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin:20px 0}.card{background:#0e1b30;border:1px solid #203451;border-radius:14px;padding:18px}.value{font-size:22px;font-weight:700;color:#f8fafc;margin-top:6px}.panel{background:#0e1b30;border:1px solid #203451;border-radius:14px;padding:20px;margin-top:16px}table{width:100%;border-collapse:collapse;margin-top:12px}th{text-align:left;color:#79e4c1;border-bottom:1px solid #27405f;padding:10px}td{border-bottom:1px solid #172a43;padding:9px;vertical-align:top}code{color:#bae6fd;word-break:break-all}.notice{border-left:4px solid #35b98e;padding:12px 16px;background:#064e3b55;border-radius:8px;margin:18px 0}@media(max-width:800px){.grid{grid-template-columns:1fr 1fr}}</style></head><body><main>")
            .Append("<section class=\"hero\"><div class=\"eyebrow\">Biblidéo · Rapport d’exécution</div><h1>")
            .Append(E(result.PackageName)).Append("</h1><div class=\"muted\">Terminé le ")
            .Append(E(result.CompletedAt.ToString("dd/MM/yyyy à HH:mm:ss"))).Append("</div><div class=\"badge\">COPIE CONTRÔLÉE RÉUSSIE</div></section>")
            .Append("<div class=\"notice\"><strong>Politique appliquée :</strong> création et copie uniquement. Aucun fichier existant n’a été écrasé, supprimé, renommé ou déplacé. Chaque copie a été vérifiée par SHA-256.</div>")
            .Append("<section class=\"grid\">")
            .Append(Card("Variante", plan.Variant.ToString()))
            .Append(Card("Fichiers copiés", result.Files.Count.ToString("N0")))
            .Append(Card("Volume vérifié", FormatBytes(result.TotalBytes)))
            .Append(Card("Durée", $"{duration.TotalSeconds:0.0} s"))
            .Append("</section><section class=\"panel\"><h2>Destination créée</h2><p><code>")
            .Append(E(result.TargetRoot)).Append("</code></p><p><b>Manifeste :</b> <code>")
            .Append(E(result.ManifestPath)).Append("</code></p></section>")
            .Append("<section class=\"panel\"><h2>Fichiers copiés et vérifiés</h2><table><thead><tr><th>Destination relative</th><th>Taille</th><th>SHA-256</th><th>Source</th></tr></thead><tbody>");

        foreach (var file in result.Files)
        {
            builder.Append("<tr><td><code>").Append(E(file.RelativeDestination)).Append("</code></td><td>")
                .Append(E(FormatBytes(file.Bytes))).Append("</td><td><code>").Append(E(file.Sha256)).Append("</code></td><td><code>")
                .Append(E(file.SourcePath)).Append("</code></td></tr>");
        }
        builder.Append("</tbody></table></section></main></body></html>");
        return builder.ToString();
    }

    public string BuildProductPackageModificationPreviewReport(ProductPackageModificationPlan plan)
    {
        var builder = BeginModernReport(
            "Simulation de modification",
            plan.ExistingPackage.Name,
            $"Mode des fichiers conservés : {(plan.CopyMode == ExistingFileCopyMode.Overwrite ? "actualisation depuis la bibliothèque" : "conservation de la copie actuelle")}",
            "Le package est encore intact. Pour l’instant, les fichiers font seulement semblant de déménager.");
        builder.Append("<div class=\"notice warn\"><strong>Simulation uniquement :</strong> aucune copie, actualisation ou mise en corbeille n’a encore été effectuée.</div>")
            .Append("<section class=\"grid\">")
            .Append(ModernCard("Ajoutés", plan.AddedComponents.Count.ToString("N0"), "ok"))
            .Append(ModernCard("Retirés", plan.RemovedComponents.Count.ToString("N0"), "danger"))
            .Append(ModernCard("Conservés", plan.RetainedComponents.Count.ToString("N0"), ""))
            .Append(ModernCard("Orphelins", plan.OrphanComponents.Count.ToString("N0"), "warn"))
            .Append("</section>");
        AppendChangeDetails(builder, "Contenu d’origine du package", plan.ExistingPackage.ComponentRelativeTopPaths, "", open: false);
        AppendChangeDetails(builder, "Composants ajoutés", plan.AddedComponents, "ok", open: true);
        AppendChangeDetails(builder, "Composants retirés", plan.RemovedComponents, "danger", open: true);
        AppendChangeDetails(builder, "Composants conservés", plan.RetainedComponents, "", open: false);
        AppendChangeDetails(builder, "Absents de la source", plan.OrphanComponents, "warn", open: plan.OrphanComponents.Count > 0);
        AppendMessagesAsDetails(builder, "Avertissements", plan.Warnings, "warn");
        AppendMessagesAsDetails(builder, "Erreurs bloquantes", plan.BlockingErrors, "danger");
        AppendActionDetails(builder, plan.FileActions);
        return EndModernReport(builder);
    }

    public string BuildProductPackageModificationExecutionReport(ProductPackageModificationPlan plan, ProductPackageModificationResult result)
    {
        var builder = BeginModernReport(
            "Modification contrôlée terminée",
            result.PackageName,
            $"Exécutée le {result.CompletedAt:dd/MM/yyyy à HH:mm:ss}",
            "Le package a changé. Les bibliothèques sources, elles, n’ont même pas vu passer le tournevis.");
        builder.Append("<div class=\"notice\"><strong>Protection appliquée :</strong> toutes les écritures sont restées enfermées dans le package. Les anciennes versions remplacées ou retirées sont récupérables.</div>")
            .Append("<section class=\"grid\">")
            .Append(ModernCard("Ajoutés", plan.AddedComponents.Count.ToString("N0"), "ok"))
            .Append(ModernCard("Retirés", plan.RemovedComponents.Count.ToString("N0"), "danger"))
            .Append(ModernCard("Actualisés", result.Files.Count(file => file.Action == PackageFileActionKind.Overwrite).ToString("N0"), ""))
            .Append(ModernCard("Préservés", plan.PreservedFileCount.ToString("N0"), ""))
            .Append("</section><section class=\"panel\"><h2>Traçabilité</h2><p><b>Package :</b> <code>")
            .Append(E(result.PackagePath)).Append("</code></p><p><b>Sauvegarde récupérable :</b> <code>")
            .Append(E(string.IsNullOrEmpty(result.RecoveryPath) ? "Aucune sauvegarde nécessaire" : result.RecoveryPath))
            .Append("</code></p><p><b>Manifeste :</b> <code>").Append(E(result.ManifestPath)).Append("</code></p></section>");
        AppendChangeDetails(builder, "Contenu d’origine du package", plan.ExistingPackage.ComponentRelativeTopPaths, "", false);
        AppendChangeDetails(builder, "Composants ajoutés", plan.AddedComponents, "ok", true);
        AppendChangeDetails(builder, "Composants retirés", plan.RemovedComponents, "danger", true);
        builder.Append("<details open><summary>Fichiers effectivement traités</summary><div class=\"detail-content\"><table><thead><tr><th>Action</th><th>Fichier</th><th>Taille</th><th>SHA-256</th></tr></thead><tbody>");
        foreach (var file in result.Files)
            builder.Append("<tr><td>").Append(ActionPill(file.Action)).Append("</td><td><code>").Append(E(file.RelativePath)).Append("</code></td><td>").Append(E(FormatBytes(file.Bytes))).Append("</td><td><code>").Append(E(file.Sha256 ?? "Déplacé vers la sauvegarde")) .Append("</code></td></tr>");
        builder.Append("</tbody></table></div></details>");
        return EndModernReport(builder);
    }

    public string BuildProductPackageTrashReport(ProductPackageTrashResult result)
    {
        var builder = BeginModernReport(
            "Package placé en corbeille",
            result.PackageName,
            $"Opération effectuée le {result.MovedAt:dd/MM/yyyy à HH:mm:ss}",
            "Il n’est pas supprimé. Il prend juste quelques vacances dans un dossier très surveillé.");
        builder.Append("<div class=\"notice\"><strong>Suppression récupérable :</strong> le package complet a été déplacé, sans toucher aux bibliothèques sources.</div><section class=\"grid\">")
            .Append(ModernCard("Composants", result.ComponentCount.ToString("N0"), ""))
            .Append(ModernCard("Fichiers", result.FileCount.ToString("N0"), ""))
            .Append(ModernCard("Source touchée", "0", "ok"))
            .Append(ModernCard("Récupérable", "Oui", "ok"))
            .Append("</section><section class=\"panel\"><h2>Emplacements</h2><p><b>Ancien :</b> <code>")
            .Append(E(result.OriginalPath)).Append("</code></p><p><b>Corbeille :</b> <code>").Append(E(result.TrashPath)).Append("</code></p></section>");
        return EndModernReport(builder);
    }

    private static StringBuilder BeginModernReport(string eyebrow, string title, string subtitle, string quip) =>
        new StringBuilder("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta name=\"viewport\" content=\"width=device-width\"><title>Rapport Biblidéo</title><style>")
            .Append(ModernReportStyle).Append("</style></head><body><main><section class=\"hero\"><div class=\"eyebrow\">")
            .Append(E(eyebrow)).Append("</div><h1>").Append(E(title)).Append("</h1><div class=\"muted\">").Append(E(subtitle))
            .Append("</div><div class=\"quip\">« ").Append(E(quip)).Append(" »</div></section>");

    private static string EndModernReport(StringBuilder builder) => builder.Append("</main></body></html>").ToString();
    private static string ModernCard(string label, string value, string css) => $"<div class=\"card\"><div class=\"muted\">{E(label)}</div><div class=\"value {css}\">{E(value)}</div></div>";

    private static void AppendChangeDetails(StringBuilder builder, string title, IReadOnlyList<string> items, string css, bool open)
    {
        builder.Append("<details").Append(open ? " open" : string.Empty).Append("><summary>").Append(E(title)).Append(" <span class=\"pill\">").Append(items.Count.ToString("N0")).Append("</span></summary><div class=\"detail-content ").Append(css).Append("\"><ul>");
        if (items.Count == 0) builder.Append("<li>Aucun élément</li>");
        foreach (var item in items) builder.Append("<li><code>").Append(E(item)).Append("</code></li>");
        builder.Append("</ul></div></details>");
    }

    private static void AppendMessagesAsDetails(StringBuilder builder, string title, IReadOnlyList<string> items, string css)
    {
        if (items.Count == 0) return;
        AppendChangeDetails(builder, title, items, css, true);
    }

    private static void AppendActionDetails(StringBuilder builder, IReadOnlyList<PackageFileAction> actions)
    {
        builder.Append("<details><summary>Plan détaillé des fichiers <span class=\"pill\">").Append(actions.Count.ToString("N0")).Append("</span></summary><div class=\"detail-content\"><table><thead><tr><th>Action</th><th>Destination</th><th>Motif</th></tr></thead><tbody>");
        foreach (var action in actions.OrderBy(action => action.Kind).ThenBy(action => action.RelativePath, StringComparer.OrdinalIgnoreCase))
            builder.Append("<tr><td>").Append(ActionPill(action.Kind)).Append("</td><td><code>").Append(E(action.RelativePath)).Append("</code></td><td>").Append(E(action.Reason)).Append("</td></tr>");
        builder.Append("</tbody></table></div></details>");
    }

    private static string ActionPill(PackageFileActionKind action) => action switch
    {
        PackageFileActionKind.Add => "<span class=\"pill ok\">AJOUT</span>",
        PackageFileActionKind.Overwrite => "<span class=\"pill\">ACTUALISATION</span>",
        PackageFileActionKind.Remove => "<span class=\"pill danger\">RETRAIT</span>",
        PackageFileActionKind.Preserve => "<span class=\"pill\">CONSERVÉ</span>",
        _ => "<span class=\"pill warn\">NON GÉRÉ</span>"
    };

    private static void AppendMessages(StringBuilder builder, string title, IReadOnlyList<string> messages, string cssClass)
    {
        if (messages.Count == 0) return;
        builder.Append("<section class=\"panel\"><h2>").Append(E(title)).Append("</h2><ul class=\"").Append(cssClass).Append("\">");
        foreach (var message in messages)
        {
            builder.Append("<li>").Append(E(message)).Append("</li>");
        }
        builder.Append("</ul></section>");
    }

    private static string Card(string label, string value) =>
        $"<div class=\"card\"><div class=\"muted\">{E(label)}</div><div class=\"value\">{E(value)}</div></div>";

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
