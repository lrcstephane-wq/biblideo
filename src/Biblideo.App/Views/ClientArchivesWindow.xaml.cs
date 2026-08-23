using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Services;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public partial class ClientArchivesWindow : Window
{
    private readonly ClientModuleContext _settings;
    private readonly ClientRegistry _registry;
    private readonly ClientRegistryService _registryService;
    private readonly ClientManagementService _managementService;

    public bool HasRestoredClient { get; private set; }

    public ClientArchivesWindow(ClientModuleContext settings, ClientRegistry registry, ClientRegistryService registryService, ClientManagementService managementService)
    {
        _settings = settings;
        _registry = registry;
        _registryService = registryService;
        _managementService = managementService;
        InitializeComponent();
        RefreshList();
    }

    private void RefreshList()
    {
        var archives = _registry.Clients.Where(client => client.IsArchived).OrderBy(client => client.FolderName).ToArray();
        ArchivesList.ItemsSource = archives;
        EmptyText.Visibility = archives.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClientMetadata metadata }) return;
        if (!IdeoDialog.Confirm("Restaurer le client", $"Le dossier {metadata.FolderName} va retrouver sa place dans la racine clients.", "Les archives rendent les dossiers. Elles ne posent presque jamais de questions.", "Restaurer")) return;
        try
        {
            var targetPath = _managementService.RestoreArchivedClient(_settings, metadata);
            metadata.IsArchived = false;
            metadata.ArchivedPath = null;
            metadata.UpdatedAtUtc = DateTime.UtcNow;
            await _registryService.SaveAsync(_settings.ClientRegistryPath, _registry);
            await _registryService.AppendHistoryAsync(_settings.ClientHistoryPath, new ClientHistoryEntry { Action = "restore", ClientName = metadata.FolderName, After = targetPath });
            HasRestoredClient = true;
            RefreshList();
        }
        catch (Exception exception)
        {
            IdeoDialog.Error("Restauration impossible", exception.Message, "Le dossier a décidé de prolonger son séjour aux archives.");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
