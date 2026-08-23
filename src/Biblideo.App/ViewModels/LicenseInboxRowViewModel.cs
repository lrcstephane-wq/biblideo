using System.ComponentModel;
using System.Runtime.CompilerServices;
using Biblideo.Core.Models;

namespace Biblideo.App.ViewModels;

public sealed class LicenseInboxRowViewModel : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private ClientRecord? _assignedClient;
    private string? _assignedAddress;

    public LicenseInboxRowViewModel(LicenseInboxItem item)
    {
        Item = item;
        _assignedClient = item.MatchedClient;
        _assignedAddress = item.MatchedAddress;
    }

    public LicenseInboxItem Item { get; }
    public ParsedLicenseMessage Message => Item.Message;
    public string FileName => Message.FileName;
    public string Subject => string.IsNullOrWhiteSpace(Message.Subject) ? "Sans objet" : Message.Subject;
    public string Recipients => Message.Recipients.Count == 0 ? "Destinataire non détecté" : string.Join(" · ", Message.Recipients);
    public string ExpiryDisplay => Message.ExpiryDate?.ToString("dd/MM/yyyy") ?? "Date à vérifier";
    public ClientRecord? AssignedClient => _assignedClient;
    public string? AssignedAddress => _assignedAddress;
    public bool CanDispatch => _assignedClient is not null && string.IsNullOrWhiteSpace(Message.Error);
    public string ClientDisplay => _assignedClient?.DisplayName ?? Item.MatchStatus switch
    {
        LicenseMatchStatus.Ambiguous => "Plusieurs clients possibles",
        LicenseMatchStatus.Unreadable => "Mail illisible",
        _ => "Client à identifier"
    };
    public string MatchDisplay => _assignedClient is not null
        ? Item.MatchedClient is null ? "Association manuelle" : "Correspondance automatique"
        : Item.MatchStatus switch
        {
            LicenseMatchStatus.Ambiguous => "Correspondance ambiguë",
            LicenseMatchStatus.Unreadable => Message.Error ?? "Lecture impossible",
            _ => "Aucune adresse connue"
        };

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public void Assign(ClientRecord client, string? address)
    {
        _assignedClient = client;
        _assignedAddress = address;
        OnPropertyChanged(nameof(AssignedClient));
        OnPropertyChanged(nameof(AssignedAddress));
        OnPropertyChanged(nameof(CanDispatch));
        OnPropertyChanged(nameof(ClientDisplay));
        OnPropertyChanged(nameof(MatchDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
