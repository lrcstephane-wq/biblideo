using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Biblideo.App.Views;

public enum IdeoDialogKind { Information, Warning, Error, Confirmation }

public partial class IdeoDialogWindow : Window
{
    public IdeoDialogWindow(string title, string message, IdeoDialogKind kind, string? aside, string primaryLabel, string? secondaryLabel)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        AsideText.Text = aside ?? string.Empty;
        AsideBorder.Visibility = string.IsNullOrWhiteSpace(aside) ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = primaryLabel;
        SecondaryButton.Content = secondaryLabel ?? string.Empty;
        SecondaryButton.Visibility = secondaryLabel is null ? Visibility.Collapsed : Visibility.Visible;

        (IconText.Text, IconCircle.Background, IconText.Foreground) = kind switch
        {
            IdeoDialogKind.Error => ("×", Brush("#5C2630"), Brush("#FFADB9")),
            IdeoDialogKind.Warning => ("!", Brush("#5A3B20"), Brush("#FFD092")),
            IdeoDialogKind.Confirmation => ("?", Brush("#23456B"), Brush("#A9D0FF")),
            _ => ("i", Brush("#1D4C61"), Brush("#A9E3F6"))
        };
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private void PrimaryButton_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void SecondaryButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void CloseButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { DialogResult = false; Close(); } }
}
