using System.Windows.Controls;

namespace Biblideo.App.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView(string title)
    {
        InitializeComponent();
        ModuleTitle.Text = title;
    }
}
