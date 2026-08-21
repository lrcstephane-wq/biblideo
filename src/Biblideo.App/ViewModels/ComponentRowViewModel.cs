using System.ComponentModel;
using System.Runtime.CompilerServices;
using Biblideo.Core.Models;

namespace Biblideo.App.ViewModels;

public sealed class ComponentRowViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public ComponentDescriptor Component { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string Library => Component.LibraryName;
    public string Family => Component.FamilyName;
    public string Type => Component.TypeName;
    public string Name => Component.ComponentName;
    public string TechnicalName => Component.TechnicalName;
    public int AssociatedCount => Component.AssociatedFiles.Count;
    public string Status => Component.Warnings.Count == 0 ? "OK" : "À contrôler";

    public ComponentRowViewModel(ComponentDescriptor component) => Component = component;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
