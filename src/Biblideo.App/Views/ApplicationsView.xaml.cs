using System.Windows;
using System.Windows.Controls;

namespace Biblideo.App.Views;

public partial class ApplicationsView : UserControl
{
    private readonly Action<string> _navigate;

    public ApplicationsView(Action<string> navigate)
    {
        _navigate = navigate;
        InitializeComponent();
    }

    private void OpenSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string destination })
            _navigate(destination);
    }
}
