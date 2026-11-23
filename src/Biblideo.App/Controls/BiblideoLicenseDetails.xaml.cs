using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Biblideo.App.Services;
using Biblideo.App.ViewModels;
using Biblideo.Core.Models;

namespace Biblideo.App.Controls;

public partial class BiblideoLicenseDetails : UserControl
{
    private readonly OutlookMsgReader _reader = new();
    private ClientRecord? _client;

    public BiblideoLicenseDetails() => InitializeComponent();

    internal ClientModuleContext? Context { get; set; }
    internal event EventHandler? UsageChanged;

    internal void ShowLoading(string message)
    {
        _client = null;
        KeysList.ItemsSource = null;
        CountText.Text = message;
        NoticeText.Text = string.Empty;
    }

    internal void Show(ClientLicenseSummary summary, NexusBiblideoUsage usage)
    {
        _client = summary.Client;
        var view = new ClientLicenseRowViewModel(summary);
        CountText.Text = view.ValidLicenseDisplay;
        var notices = new List<string>();
        if (summary.Licenses.Count == 0) notices.Add("Aucune clé identifiée.");
        if (summary.UnreadableCount > 0) notices.Add($"{summary.UnreadableCount} mail(s) sans clé ou échéance lisible.");
        if (usage.Notice is not null) notices.Add(usage.Notice);
        NoticeText.Text = string.Join("\n", notices);
        var today = DateTime.Today;
        KeysList.ItemsSource = summary.Licenses.Select(detail =>
        {
            var expired = detail.License.ExpiryDate.Date < today;
            var status = detail.IsUnused ? "Inutilisée" : expired ? "Expirée" : "Valide";
            var brush = detail.IsUnused ? "MutedBrush" : expired ? "WarningBrush" : "SuccessBrush";
            return new DetailRow(detail, $"Échéance · {detail.License.ExpiryDate:dd/MM/yyyy}", status, (Brush)FindResource(brush),
                usage.CanEdit, detail.IsUnused ? "Remettre en suivi" : "Marquer inutilisée");
        }).ToArray();
    }

    private async void RevealKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BiblideoLicenseDetail detail } button) return;
        var client = _client;
        button.IsEnabled = false;
        try
        {
            var key = await _reader.ReadKeyAsync(detail.SourcePath, detail.License.KeyId);
            if (_client != client || !IsVisible) return;
            if (key is not null) IdeoDialog.Info("Clé Biblidéo", key);
            else IdeoDialog.Warning("Clé introuvable", "Actualise la fiche pour relire les mails archivés.");
        }
        catch (Exception error)
        {
            if (_client == client && IsVisible) IdeoDialog.Warning("Clé indisponible", error.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void SetUsage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BiblideoLicenseDetail detail } || _client is not { } client || Context is not { } context) return;
        var unused = !detail.IsUnused;
        if (unused && !IdeoDialog.Confirm("Marquer la clé comme inutilisée",
                $"La clé {detail.License.KeyLabel} de {client.DisplayName} sera exclue du compteur et des alertes. Son échéance et son mail seront conservés.",
                primaryLabel: "Marquer inutilisée")) return;

        IsEnabled = false;
        try
        {
            await NexusRegistryClient.SetBiblideoUsageAsync(context, client.MetadataId, detail.License.KeyId, unused);
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

    private sealed record DetailRow(BiblideoLicenseDetail Detail, string DateLabel, string StatusLabel, Brush StatusBrush,
        bool CanEdit, string ActionLabel);
}
