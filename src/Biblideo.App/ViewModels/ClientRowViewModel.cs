using System.ComponentModel;
using System.Runtime.CompilerServices;
using Biblideo.Core.Models;
using Biblideo.App.Services;

namespace Biblideo.App.ViewModels;

public sealed class ClientRowViewModel : INotifyPropertyChanged
{
    private bool _desiredSubscribed;

    public ClientRowViewModel(ClientRecord client, IReadOnlyList<NexusAtlasRight>? atlasRights = null)
    {
        Client = client;
        _desiredSubscribed = client.IsSubscribed;
        AtlasRights = atlasRights ?? [];
        AtlasStatus = GetAtlasStatus(atlasRights, DateOnly.FromDateTime(DateTime.Today));
    }

    public ClientRecord Client { get; }
    public IReadOnlyList<NexusAtlasRight> AtlasRights { get; }
    public string AtlasStatus { get; }

    internal static string GetAtlasStatus(IReadOnlyList<NexusAtlasRight>? rights, DateOnly today)
    {
        if (rights is null) return "Atlas · Indisponible";
        if (rights.Count == 0) return "Atlas · Aucune licence";
        var active = rights.Count(right => right.IsActiveOn(today));
        return active == 0 ? "Atlas · Licences expirées" : $"Atlas · {active}/{rights.Count} poste(s) actif(s)";
    }

    public string DisplayName => Client.DisplayName;
    public string Initials
    {
        get
        {
            var words = Client.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }
    public string FolderName => Client.FolderName;
    public string VariantLabel => Client.Variant.ToFolderToken();
    public string LatestDeployment => Client.LatestDeploymentDisplay;
    public string EmailSummary => Client.Emails.Count switch
    {
        0 => "Aucune adresse renseignée",
        1 => Client.Emails[0],
        _ => $"{Client.Emails.Count} adresses · {Client.Emails[0]}"
    };
    public string StructureLabel => Client.StructureIsValid
        ? "Structure conforme"
        : $"{Client.MissingFolders.Count} dossier(s) manquant(s)";
    public bool CanRepairStructure => !Client.StructureIsValid;

    public bool DesiredSubscribed
    {
        get => _desiredSubscribed;
        set
        {
            if (_desiredSubscribed == value) return;
            _desiredSubscribed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPendingChange));
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public bool HasPendingChange => DesiredSubscribed != Client.IsSubscribed;
    public string StatusLabel => HasPendingChange
        ? DesiredSubscribed ? "Activation en attente" : "Désactivation en attente"
        : DesiredSubscribed ? "Mises à jour actives" : "Hors mises à jour";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
