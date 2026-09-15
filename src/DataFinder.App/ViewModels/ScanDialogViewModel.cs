using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using DataFinder.App.Infrastructure;
using DataFinder.Core.Models;
using DataFinder.Core.Util;
using DataFinder.Core.Volumes;

namespace DataFinder.App.ViewModels;

/// <summary>
/// The state of the *Scan...* dialog: which drives to read and what a folder has to look like to be
/// reported. It is built once and kept, so the ticks and the rule values are still there the next
/// time the dialog is opened.
/// </summary>
public sealed class ScanDialogViewModel : ObservableObject
{
    private string _minSizeText = "200";
    private string _minFileCountText = "200";
    private bool _sizeIncludesSubfolders = true;
    private string? _validationMessage;
    private bool _isElevated;

    public ScanDialogViewModel()
    {
        RefreshVolumesCommand = new RelayCommand(RefreshVolumes);
        SelectAllDrivesCommand = new RelayCommand(() => SetAllDrivesSelected(true), () => VolumeChoices.Count > 0);
        SelectNoDrivesCommand = new RelayCommand(() => SetAllDrivesSelected(false), () => SelectedDriveCount > 0);
        ScanCommand = new RelayCommand(RequestScan, () => CanScan);

        ValidateSettings();
    }

    /// <summary>Raised when the user asks for the scan, so the dialog can close and let it start.</summary>
    public event Action? ScanRequested;

    public ObservableCollection<VolumeChoice> VolumeChoices { get; } = new();

    public RelayCommand RefreshVolumesCommand { get; }

    public RelayCommand SelectAllDrivesCommand { get; }

    public RelayCommand SelectNoDrivesCommand { get; }

    public RelayCommand ScanCommand { get; }

    public string MinSizeText
    {
        get => _minSizeText;
        set
        {
            if (SetProperty(ref _minSizeText, value))
            {
                ValidateSettings();
            }
        }
    }

    public string MinFileCountText
    {
        get => _minFileCountText;
        set
        {
            if (SetProperty(ref _minFileCountText, value))
            {
                ValidateSettings();
            }
        }
    }

    public bool SizeIncludesSubfolders
    {
        get => _sizeIncludesSubfolders;
        set
        {
            if (SetProperty(ref _sizeIncludesSubfolders, value))
            {
                ValidateSettings();
            }
        }
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
                OnPropertyChanged(nameof(CanScan));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>False when the app has to tell the user that raw volume reads need administrator rights.</summary>
    public bool IsElevated
    {
        get => _isElevated;
        set
        {
            if (SetProperty(ref _isElevated, value))
            {
                OnPropertyChanged(nameof(NeedsElevation));
            }
        }
    }

    public bool NeedsElevation => !IsElevated;

    public IReadOnlyList<VolumeInfo> SelectedVolumes =>
        VolumeChoices.Where(choice => choice.IsSelected).Select(choice => choice.Volume).ToList();

    public int SelectedDriveCount => VolumeChoices.Count(choice => choice.IsSelected);

    public string DriveSelectionText => SelectedDriveCount switch
    {
        0 => "No drive selected",
        1 => "1 drive selected",
        _ => $"{SelectedDriveCount} drives selected",
    };

    /// <summary>True when there is something to scan and the rule values make sense.</summary>
    public bool CanScan => SelectedDriveCount > 0 && !HasValidationMessage;

    /// <summary>Reads the drives that are mounted now, keeping the ticks of the drives that are left.</summary>
    public void RefreshVolumes()
    {
        var previouslySelected = VolumeChoices
            .Where(choice => choice.IsSelected)
            .Select(choice => choice.Volume.DriveLetter)
            .ToHashSet();

        foreach (VolumeChoice choice in VolumeChoices)
        {
            choice.PropertyChanged -= OnVolumeChoiceChanged;
        }

        VolumeChoices.Clear();

        var found = new List<VolumeChoice>();
        foreach (VolumeInfo volume in VolumeEnumerator.GetNtfsVolumes())
        {
            // A refresh keeps the ticks that are still there. The first run ticks the first drive.
            bool selected = previouslySelected.Count > 0
                ? previouslySelected.Contains(volume.DriveLetter)
                : found.Count == 0;

            var choice = new VolumeChoice(volume, selected);
            choice.PropertyChanged += OnVolumeChoiceChanged;
            found.Add(choice);
            VolumeChoices.Add(choice);
        }

        OnDriveSelectionChanged();
    }

    public bool TryBuildSettings(out ScanSettings settings, out string? error) =>
        ScanSettings.TryParse(MinSizeText, MinFileCountText, SizeIncludesSubfolders, out settings, out error);

    /// <summary>
    /// The rules and the drives of the next run in one line, for the status bar of the main window -
    /// it is the only place left that says what the results below it were looked for with.
    /// </summary>
    public string DescribeSelection()
    {
        IReadOnlyList<VolumeInfo> volumes = SelectedVolumes;
        string drives = volumes.Count == 0
            ? "no drives"
            : string.Join(", ", volumes.Select(volume => volume.DriveLetter + ":"));

        if (!TryBuildSettings(out ScanSettings settings, out _))
        {
            return drives;
        }

        string scope = settings.SizeIncludesSubfolders ? "whole folder" : "direct files only";
        return
            $"{drives}: size > {ByteSize.Format(settings.MinSizeBytes)} ({scope}), " +
            $"> {settings.MinDirectFileCount:N0} files directly inside";
    }

    private void RequestScan()
    {
        if (CanScan)
        {
            ScanRequested?.Invoke();
        }
    }

    private void OnVolumeChoiceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VolumeChoice.IsSelected))
        {
            OnDriveSelectionChanged();
        }
    }

    private void OnDriveSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedVolumes));
        OnPropertyChanged(nameof(SelectedDriveCount));
        OnPropertyChanged(nameof(DriveSelectionText));
        OnPropertyChanged(nameof(CanScan));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetAllDrivesSelected(bool selected)
    {
        foreach (VolumeChoice choice in VolumeChoices)
        {
            choice.IsSelected = selected;
        }

        OnDriveSelectionChanged();
    }

    private void ValidateSettings()
    {
        ValidationMessage = ScanSettings.TryParse(MinSizeText, MinFileCountText, SizeIncludesSubfolders, out _, out string? error)
            ? null
            : error;
    }
}
