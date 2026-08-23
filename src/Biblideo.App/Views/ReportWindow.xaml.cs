using System.Text;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Biblideo.App.Services;

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
        IdeoDialog.Info("Rapport exporté", "Le rapport HTML a bien été enregistré.", "Celui-là peut sortir : il a tous ses papiers.");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
