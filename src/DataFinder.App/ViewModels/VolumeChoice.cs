using DataFinder.App.Infrastructure;
using DataFinder.Core.Models;

namespace DataFinder.App.ViewModels;

/// <summary>One drive in the list the user ticks to scan, together with its tick.</summary>
public sealed class VolumeChoice : ObservableObject
{
    private bool _isSelected;

    public VolumeChoice(VolumeInfo volume, bool isSelected)
    {
        Volume = volume;
        _isSelected = isSelected;
    }

    public VolumeInfo Volume { get; }

    public string DriveLetter => Volume.DriveLetter.ToString();

    public string DisplayName => Volume.DisplayName;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
