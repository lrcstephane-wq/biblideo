using System.Runtime.InteropServices;
using System.Text;
using Biblideo.Core.Models;

namespace Biblideo.App.Services;

public sealed class OutlookNotificationService
{
    public void SendSubscriptionChanges(
        IReadOnlyList<ClientSubscriptionChange> changes,
        IReadOnlyList<string> recipients)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("L’envoi Outlook est disponible uniquement sous Windows.");
        if (NexusPreviewProfile.Enabled || changes.Count == 0) return;
        if (recipients.Count == 0)
            throw new InvalidOperationException("Aucun destinataire n’est configuré pour les notifications clients.");

        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
                          ?? throw new InvalidOperationException("Outlook classique n’est pas installé ou n’est pas accessible sur ce poste.");
        object? outlook = null;
        object? mail = null;
        try
        {
            outlook = Activator.CreateInstance(outlookType)
                      ?? throw new InvalidOperationException("Impossible de démarrer Outlook classique.");
            dynamic outlookApplication = outlook;
            mail = outlookApplication.CreateItem(0);
            dynamic message = mail;
            message.To = string.Join(";", recipients.Where(address => !string.IsNullOrWhiteSpace(address)));
            message.Subject = changes.Count == 1
                ? $"[Biblidéo] Statut MAJ modifié — {changes[0].Client.DisplayName}"
                : $"[Biblidéo] {changes.Count} statuts de mise à jour modifiés";
            message.HTMLBody = BuildBody(changes);
            message.Send();
        }
        finally
        {
            if (mail is not null && Marshal.IsComObject(mail)) Marshal.FinalReleaseComObject(mail);
            if (outlook is not null && Marshal.IsComObject(outlook)) Marshal.FinalReleaseComObject(outlook);
        }
    }

    private static string BuildBody(IReadOnlyList<ClientSubscriptionChange> changes)
    {
        var rows = new StringBuilder();
        foreach (var change in changes)
        {
            rows.Append("<tr>")
                .Append($"<td style='padding:10px 12px;border-bottom:1px solid #dbe3ec'><strong>{Escape(change.Client.DisplayName)}</strong></td>")
                .Append($"<td style='padding:10px 12px;border-bottom:1px solid #dbe3ec'>{Escape(change.Client.Variant.ToFolderToken())}</td>")
                .Append($"<td style='padding:10px 12px;border-bottom:1px solid #dbe3ec'>{(change.Client.IsSubscribed ? "Avec MAJ" : "Sans MAJ")}</td>")
                .Append($"<td style='padding:10px 12px;border-bottom:1px solid #dbe3ec;color:{(change.NewStatus ? "#138a5b" : "#b65a25")};font-weight:700'>{(change.NewStatus ? "Avec MAJ" : "Sans MAJ")}</td>")
                .Append("</tr>");
        }

        return $"""
            <html><body style="font-family:Segoe UI,Arial,sans-serif;background:#f3f6fa;color:#142033;padding:24px">
            <div style="max-width:760px;margin:auto;background:white;border:1px solid #dbe3ec;border-radius:12px;overflow:hidden">
              <div style="background:#11253f;color:white;padding:20px 24px"><h2 style="margin:0">Biblidéo · Gestion clients</h2></div>
              <div style="padding:22px 24px">
                <p>Une modification du statut de mise à jour vient d’être appliquée.</p>
                <table style="border-collapse:collapse;width:100%;margin-top:18px">
                  <thead><tr style="background:#eaf1f9;text-align:left"><th style="padding:10px 12px">Client</th><th style="padding:10px 12px">Version</th><th style="padding:10px 12px">Avant</th><th style="padding:10px 12px">Maintenant</th></tr></thead>
                  <tbody>{rows}</tbody>
                </table>
                <p style="font-size:12px;color:#66758a;margin-top:22px">Modification effectuée le {DateTime.Now:dd/MM/yyyy à HH:mm} depuis {Escape(Environment.MachineName)} par {Escape(Environment.UserName)}.</p>
                <p style="font-size:12px;color:#66758a;font-style:italic">Les engrenages ont pris note. Ils aiment beaucoup les dossiers bien rangés.</p>
              </div>
            </div></body></html>
            """;
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}
