using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Biblideo.Core.Models;
using Biblideo.App.ViewModels;
using Biblideo.Core.Services;

namespace Biblideo.App.Views;

public partial class ClientEditorWindow : Window
{
    private readonly string _marker;
    private readonly bool _editing;

    public ClientDraft? Draft { get; private set; }

    public ClientEditorWindow(string marker, ClientRowViewModel? row = null)
    {
        var existing = row?.Client;
        _marker = marker;
        _editing = existing is not null;
        InitializeComponent();
        VariantCombo.SelectedIndex = existing?.Variant switch
        {
            ClientLibraryVariant.EP => 1,
            ClientLibraryVariant.SC_EP => 2,
            _ => 0
        };
        SubscriptionToggle.IsChecked = existing?.IsSubscribed ?? true;
        if (existing is not null)
        {
            TitleText.Text = "Modifier le client";
            SaveButton.Content = "Enregistrer";
            NameText.Text = existing.BaseName.Replace('_', ' ');
            EmailsText.Text = string.Join(Environment.NewLine, existing.Emails);
        }
        if (row is not null)
        {
            AtlasPanel.DataContext = row;
            AtlasPanel.Visibility = Visibility.Visible;
        }
        UpdatePreview();
    }

    private void FormChanged(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (!IsInitialized) return;
        SubscriptionLabel.Text = SubscriptionToggle.IsChecked == true ? "Mises à jour actives" : "Hors mises à jour";
        SubscriptionLabel.Foreground = SubscriptionToggle.IsChecked == true
            ? System.Windows.Media.Brushes.LightGreen
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
        try
        {
            PreviewText.Text = ClientFolderNamingService.BuildFolderName(NameText.Text, SelectedVariant(), SubscriptionToggle.IsChecked == true, _marker);
            ValidationText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            PreviewText.Text = "—";
            ValidationText.Text = exception.Message;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var emails = EmailsText.Text
                .Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (emails.Length == 0) throw new InvalidOperationException("Au moins une adresse mail est obligatoire.");
            foreach (var email in emails) _ = new MailAddress(email);
            _ = ClientFolderNamingService.BuildFolderName(NameText.Text, SelectedVariant(), SubscriptionToggle.IsChecked == true, _marker);
            Draft = new ClientDraft(NameText.Text, SelectedVariant(), SubscriptionToggle.IsChecked == true, emails);
            DialogResult = true;
        }
        catch (FormatException)
        {
            ValidationText.Text = "Une adresse mail n’est pas valide. Outlook refuse les adresses créatives.";
        }
        catch (Exception exception)
        {
            ValidationText.Text = exception.Message;
        }
    }

    private ClientLibraryVariant SelectedVariant() => VariantCombo.SelectedIndex switch
    {
        1 => ClientLibraryVariant.EP,
        2 => ClientLibraryVariant.SC_EP,
        _ => ClientLibraryVariant.SC
    };

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
