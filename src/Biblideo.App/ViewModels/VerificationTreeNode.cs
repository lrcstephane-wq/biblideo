using System.Collections.ObjectModel;

namespace Biblideo.App.ViewModels;

public sealed class VerificationTreeNode
{
    public required string Name { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string Glyph { get; init; } = "•";
    public string Badge { get; init; } = string.Empty;
    public bool IsExpanded { get; set; }
    public ComponentRowViewModel? Component { get; init; }
    public bool IsRemovable => Component is not null;
    public ObservableCollection<VerificationTreeNode> Children { get; init; } = [];
}
