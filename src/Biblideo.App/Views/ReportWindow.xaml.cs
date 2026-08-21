using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Biblideo.App.Views;

public partial class ReportWindow : Window
{
    private readonly string _html;
    private readonly string _suggestedFileName;

    public ReportWindow(string html, string suggestedFileName)
    {
        _html = html;
        _suggestedFileName = suggestedFileName;
        InitializeComponent();
        Loaded += (_, _) => ReportBrowser.NavigateToString(_html);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Rapport HTML|*.html", FileName = _suggestedFileName };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, _html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show("Rapport exporté.", "Biblidéo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
