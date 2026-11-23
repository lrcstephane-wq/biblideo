using Biblideo.Core.Models;

namespace Biblideo.App.ViewModels;

public sealed class ClientLicenseRowViewModel
{
    public ClientLicenseRowViewModel(ClientLicenseSummary summary) => Summary = summary;
    public ClientLicenseSummary Summary { get; }
    public ClientRecord Client => Summary.Client;
    public string ClientName => Client.DisplayName;
    public string Initials
    {
        get
        {
            var words = Client.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }
    public string Variant => Client.Variant.ToFolderToken();
    public string ExpiryDisplay => Summary.ExpiryDate?.ToString("dd/MM/yyyy") ?? "Aucune échéance";
    public string DaysDisplay => Summary.DaysRemaining switch
    {
        null => Summary.State == ClientLicenseState.Missing ? "Pas de licence" : "À vérifier",
        < 0 => $"Expirée depuis {-Summary.DaysRemaining.Value} j",
        0 => "Expire aujourd’hui",
        _ => $"{Summary.DaysRemaining} jours restants"
    };
    public string StateDisplay => Summary.State switch
    {
        ClientLicenseState.Valid => "Valide",
        ClientLicenseState.RenewSoon => "À renouveler",
        ClientLicenseState.Expired => "Expirée",
        ClientLicenseState.Missing => "Pas de licence",
        ClientLicenseState.OffSubscription => "Hors mise à jour",
        _ => "À vérifier"
    };
    public string ValidLicenseDisplay => Summary.ValidLicenseCount <= 1
        ? $"{Summary.ValidLicenseCount} licence valide"
        : $"{Summary.ValidLicenseCount} licences valides";
    public string MessageDisplay => Summary.MessageCount switch
    {
        0 => "Aucun mail archivé",
        1 => "1 mail archivé",
        _ => $"{Summary.MessageCount} mails archivés"
    };
    public string StateColor => Summary.State switch
    {
        ClientLicenseState.Valid => "#56C99B",
        ClientLicenseState.RenewSoon => "#D89B54",
        ClientLicenseState.Expired => "#EF7777",
        ClientLicenseState.OffSubscription => "#748399",
        ClientLicenseState.Unreadable => "#C68BE3",
        _ => "#7FB4EF"
    };
}
