using System.Net;
using System.Text;
using Biblideo.Core.Models;

namespace Biblideo.Core.Services;

public sealed class HtmlReportService
{
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
