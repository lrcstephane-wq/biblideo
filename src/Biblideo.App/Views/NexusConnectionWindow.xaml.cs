using System.Windows;
using System.Windows.Controls;
using Biblideo.App.Services;
using Microsoft.Win32;

namespace Biblideo.App.Views;

public partial class NexusConnectionWindow : Window
{
    public NexusConnectionWindow()
    {
        InitializeComponent();
        NasMode.IsChecked = true;
        try
        {
            var location = NexusRegistryLocation.Load();
            NasInput.Text = location.NasRoot;
            CloudInput.Text = location.CloudRoot;
            NasMode.IsChecked = !location.UseCloud;
            CloudMode.IsChecked = location.UseCloud;
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var input = sender is Button { Tag: "cloud" } ? CloudInput : NasInput;
        var dialog = new OpenFolderDialog { Title = "Choisir la racine du registre Nexus" };
        if (dialog.ShowDialog(this) == true) input.Text = dialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new NexusRegistryLocation(NasInput.Text.Trim(), CloudInput.Text.Trim(), CloudMode.IsChecked == true).Save();
            DialogResult = true;
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }
}
