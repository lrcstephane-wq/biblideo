namespace Biblideo.App.ViewModels;

public sealed class LibraryFilterNode
{
    public required string Label { get; init; }
    public string? Library { get; init; }
    public string? Family { get; init; }
    public List<LibraryFilterNode> Children { get; init; } = [];
    public override string ToString() => Label;
}
