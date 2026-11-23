using System.Windows;
using Biblideo.App.Services;

namespace Biblideo.App.Views;

public partial class NexusConnectionWindow : Window
{
    public NexusConnectionWindow()
    {
        InitializeComponent();
        AddressInput.Text = NexusRegistryClient.Address ?? string.Empty;
        DisconnectButton.IsEnabled = NexusRegistryClient.IsShared;
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        NexusRegistryClient.Disconnect();
        Close();
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = DisconnectButton.IsEnabled = false;
        try
        {
            NexusRegistryClient.Configure(AddressInput.Text, TokenInput.Password);
            await NexusRegistryClient.GetSessionAsync();
            if (IsLoaded) DialogResult = true;
        }
        catch (Exception error) when (NexusRegistryClient.ConnectionUnavailable(error))
        {
            StatusText.Text = "Service inaccessible. Ferme cette fenêtre pour consulter la copie locale si elle existe.";
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
        finally
        {
            TokenInput.Clear();
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = NexusRegistryClient.IsShared;
        }
    }
}
