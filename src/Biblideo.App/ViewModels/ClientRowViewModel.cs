using System.ComponentModel;
using System.Runtime.CompilerServices;
using Biblideo.Core.Models;

namespace Biblideo.App.ViewModels;

public sealed class ClientRowViewModel : INotifyPropertyChanged
{
    private bool _desiredSubscribed;

    public ClientRowViewModel(ClientRecord client)
    {
        Client = client;
        _desiredSubscribed = client.IsSubscribed;
    }

    public ClientRecord Client { get; }
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
