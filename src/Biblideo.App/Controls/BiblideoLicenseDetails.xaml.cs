using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Biblideo.App.Services;
using Biblideo.App.ViewModels;
using Biblideo.Core.Models;

namespace Biblideo.App.Controls;

public partial class BiblideoLicenseDetails : UserControl
{
    private readonly OutlookMsgReader _reader = new();
    private ClientRecord? _client;
    private int _displayVersion;

    public BiblideoLicenseDetails() => InitializeComponent();

    internal ClientModuleContext? Context { get; set; }
    internal event EventHandler? UsageChanged;

    internal void ShowLoading(string message)
    {
        _displayVersion++;
        _client = null;
        KeysList.ItemsSource = null;
        CountText.Text = message;
        NoticeText.Text = string.Empty;
    }

    internal void Show(ClientLicenseSummary summary, NexusBiblideoUsage usage)
    {
        var version = ++_displayVersion;
        _client = summary.Client;
        var view = new ClientLicenseRowViewModel(summary);
        CountText.Text = view.ValidLicenseDisplay;
        var notices = new List<string>();
        if (summary.Licenses.Count == 0) notices.Add("Aucune clé identifiée.");
        if (summary.UnreadableCount > 0) notices.Add($"{summary.UnreadableCount} mail(s) sans clé ou échéance lisible.");
        if (usage.Notice is not null) notices.Add(usage.Notice);
        NoticeText.Text = string.Join("\n", notices);
        DisplayRows(summary.Licenses, usage.CanEdit, "Lecture de la clé…");
        if (summary.Licenses.Count > 0)
            _ = LoadKeysAsync(summary.Licenses, usage.CanEdit, version);
    }

    private async Task LoadKeysAsync(IReadOnlyList<BiblideoLicenseDetail> details, bool canEdit, int version)
    {
        IReadOnlyDictionary<string, string>? keys = null;
        try
        {
            keys = await _reader.ReadKeysAsync(details);
        }
        catch (Exception)
        {
            // Les échéances indexées restent consultables si le mail ne peut être ouvert.
        }

        if (version == _displayVersion)
            DisplayRows(details, canEdit, "Clé indisponible", keys);
    }

    private void DisplayRows(IReadOnlyList<BiblideoLicenseDetail> details, bool canEdit, string fallback,
        IReadOnlyDictionary<string, string>? keys = null)
    {
        var today = DateTime.Today;
        KeysList.ItemsSource = details.Select(detail =>
        {
            var expired = detail.License.ExpiryDate.Date < today;
            var status = detail.IsUnused ? "Inutilisée" : expired ? "Expirée" : "Valide";
            var brush = detail.IsUnused ? "MutedBrush" : expired ? "WarningBrush" : "SuccessBrush";
            var key = keys?.GetValueOrDefault(detail.License.KeyId);
            return new DetailRow(detail, key ?? fallback, $"Échéance · {detail.License.ExpiryDate:dd/MM/yyyy}",
                status, (Brush)FindResource(brush), canEdit);
        }).ToArray();
    }

    private async void SetUsage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: DetailRow row } toggle || _client is not { } client || Context is not { } context) return;
        var detail = row.Detail;
        var unused = !detail.IsUnused;
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, row.IsFollowed);
        if (unused && !IdeoDialog.Confirm("Marquer la clé comme inutilisée",
                $"Cette clé de {client.DisplayName} sera exclue du compteur et des alertes. Son échéance et son mail seront conservés.",
                primaryLabel: "Marquer inutilisée")) return;

        IsEnabled = false;
        try
        {
            await NexusRegistryClient.SetBiblideoUsageAsync(context, client.MetadataId, detail.License.KeyId, unused);
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, !unused);
            UsageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            IdeoDialog.Warning("Suivi de la clé indisponible", error.Message);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private sealed record DetailRow(BiblideoLicenseDetail Detail, string KeyText, string DateLabel,
        string StatusLabel, Brush StatusBrush, bool CanEdit)
    {
        public bool IsFollowed => !Detail.IsUnused;
        public string FollowLabel => IsFollowed ? "Suivie" : "Inutilisée";
        public string ActionLabel => IsFollowed ? "Marquer cette clé comme inutilisée" : "Remettre cette clé en suivi";
    }
}
