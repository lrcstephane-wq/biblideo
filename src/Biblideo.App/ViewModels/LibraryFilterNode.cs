using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Biblideo.App.ViewModels;

public sealed class LibraryFilterNode : INotifyPropertyChanged
{
    private int _selectedCount;

    public required string Name { get; init; }
    public string? Library { get; init; }
    public string? Family { get; init; }
    public string? Type { get; init; }
    public int TotalCount { get; init; }
    public int SelectedCount
    {
        get => _selectedCount;
        set
        {
            if (_selectedCount == value) return;
            _selectedCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }
    public string DisplayLabel => SelectedCount == 0
        ? $"{Name} ({TotalCount:N0})"
        : $"{Name} ({SelectedCount:N0}/{TotalCount:N0})";
    public List<LibraryFilterNode> Children { get; init; } = [];
    public override string ToString() => DisplayLabel;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
