using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Biblideo.Core.Models;

namespace Biblideo.App.ViewModels;

public sealed class ComponentRowViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _thumbnailLoaded;
    private BitmapImage? _thumbnail;

    public ComponentDescriptor Component { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionBadge));
        }
    }

    public string Library => Component.LibraryName;
    public string Family => Component.FamilyName;
    public string Type => Component.TypeName;
    public string Name => Component.ComponentName;
    public string TechnicalName => Component.TechnicalName;
    public int AssociatedCount => Component.AssociatedFiles.Count;
    public int FileCount => AssociatedCount + 1;
    public string Status => Component.Warnings.Count == 0 ? "OK" : "À contrôler";
    public string SelectionBadge => IsSelected ? "Ajouté au package" : Status;
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;
            var path = Component.AssociatedFiles.FirstOrDefault(file =>
                file.EndsWith(".top.png", StringComparison.OrdinalIgnoreCase));
            if (path is null || !File.Exists(path)) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 96;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                _thumbnail = bitmap;
            }
            catch
            {
                _thumbnail = null;
            }
            return _thumbnail;
        }
    }

    public ComponentRowViewModel(ComponentDescriptor component) => Component = component;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
