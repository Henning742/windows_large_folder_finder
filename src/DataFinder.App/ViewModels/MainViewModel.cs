using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using DataFinder.App.Infrastructure;
using DataFinder.App.Services;
using DataFinder.Core.Models;
using DataFinder.Core.Ntfs;
using DataFinder.Core.Preview;
using DataFinder.Core.Results;
using DataFinder.Core.Volumes;

namespace DataFinder.App.ViewModels;

/// <summary>One entry of the "select a file on its own" drop down.</summary>
public sealed record AutoSelectOption(AutoSelectMode Mode, string Text);

public sealed class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly PreviewService _previewService = new();

    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _contentsCancellation;
    private CancellationTokenSource? _previewCancellation;

    private MftIndex? _index;
    private AggregationResult? _aggregation;
    private VolumeInfo? _scannedVolume;
    private ScanSettings? _lastSettings;

    private VolumeInfo? _selectedVolume;
    private string _minSizeText = "200";
    private string _minFileCountText = "200";
    private bool _sizeIncludesSubfolders = true;
    private string? _validationMessage;
    private string _resultFilter = string.Empty;
    private IReadOnlyList<ResultTreeNode> _resultRoots = Array.Empty<ResultTreeNode>();
    private IReadOnlyList<ResultTreeNode> _visibleResults = Array.Empty<ResultTreeNode>();
    private ResultTreeNode? _selectedResultNode;
    private FileEntry? _selectedContent;
    private AutoSelectMode _autoSelectMode = AutoSelectMode.FirstFile;
    private string _activeFolderPath = string.Empty;
    private ImageSource? _previewImage;
    private string _previewText = string.Empty;
    private string _previewMessage = "Select a folder on the left to see what is inside.";
    private bool _isImagePreviewVisible;
    private bool _isTextPreviewVisible;
    private bool _isMessageVisible = true;
    private bool _isScanning;
    private bool _isElevated;
    private double _progressValue;
    private string _statusText = "Ready. Choose a drive and press Scan.";
    private string _resultSummary = "No results yet.";
    private string _scanWarning = string.Empty;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        IsElevated = ElevationHelper.IsElevated();

        RefreshVolumesCommand = new RelayCommand(RefreshVolumes, () => !IsScanning);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && SelectedVolume is not null && !HasValidationMessage);
        CancelCommand = new RelayCommand(CancelRunningWork, () => IsScanning);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsScanning);
        ExportCommand = new RelayCommand(ExportResults, () => Results.Count > 0);
        ClearResultsCommand = new RelayCommand(ClearResults, () => Results.Count > 0 && !IsScanning);
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true), () => VisibleResults.Count > 0);
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false), () => VisibleResults.Count > 0);
        OpenFolderCommand = new RelayCommand(OpenActiveFolder, () => ActiveFolderPath.Length > 0);
        OpenContentCommand = new RelayCommand(OpenSelectedContent, () => SelectedContent is not null);
        CopyPathCommand = new RelayCommand(CopyActivePath, () => ActiveFolderPath.Length > 0);
        NavigateUpCommand = new RelayCommand(NavigateUp, () => CanNavigateUp);
        RelaunchElevatedCommand = new RelayCommand(RelaunchElevated, () => !IsElevated);

        ValidateSettings();
    }

    public ObservableCollection<VolumeInfo> Volumes { get; } = new();

    public ObservableCollection<FolderResult> Results { get; } = new();

    public ObservableCollection<FileEntry> Contents { get; } = new();

    /// <summary>The choices of the "select a file on its own" drop down, in the order they are shown.</summary>
    public IReadOnlyList<AutoSelectOption> AutoSelectOptions { get; } = new[]
    {
        new AutoSelectOption(AutoSelectMode.FirstFile, "First file"),
        new AutoSelectOption(AutoSelectMode.MiddleFile, "Middle file"),
        new AutoSelectOption(AutoSelectMode.RandomFile, "Random file"),
        new AutoSelectOption(AutoSelectMode.None, "Nothing"),
    };

    public RelayCommand RefreshVolumesCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand ClearResultsCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public RelayCommand OpenContentCommand { get; }

    public RelayCommand CopyPathCommand { get; }

    public RelayCommand NavigateUpCommand { get; }

    public RelayCommand RelaunchElevatedCommand { get; }

    public VolumeInfo? SelectedVolume
    {
        get => _selectedVolume;
        set => SetProperty(ref _selectedVolume, value);
    }

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
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public string ResultFilter
    {
        get => _resultFilter;
        set
        {
            if (SetProperty(ref _resultFilter, value))
            {
                RebuildResultTree();
            }
        }
    }

    /// <summary>
    /// The rows of the result tree that are on screen right now: a collapsed folder hides the
    /// folders below it, so this list is rebuilt whenever something is expanded or collapsed.
    /// </summary>
    public IReadOnlyList<ResultTreeNode> VisibleResults
    {
        get => _visibleResults;
        private set
        {
            if (SetProperty(ref _visibleResults, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ResultTreeNode? SelectedResultNode
    {
        get => _selectedResultNode;
        set
        {
            if (SetProperty(ref _selectedResultNode, value) && value is not null)
            {
                OnPropertyChanged(nameof(HasSelectedFolder));
                ShowFolder(value.FullPath, value.Result?.RecordNumber ?? 0);
            }
        }
    }

    /// <summary>True when the selected row stands for a folder that matched the rules.</summary>
    public bool HasSelectedFolder => SelectedResultNode?.Result is not null;

    public FileEntry? SelectedContent
    {
        get => _selectedContent;
        set
        {
            if (SetProperty(ref _selectedContent, value))
            {
                StartPreviewLoad();
            }
        }
    }

    /// <summary>Which file the app selects by itself when a folder is opened.</summary>
    public AutoSelectMode AutoSelectMode
    {
        get => _autoSelectMode;
        set => SetProperty(ref _autoSelectMode, value);
    }

    public string ActiveFolderPath
    {
        get => _activeFolderPath;
        private set
        {
            if (SetProperty(ref _activeFolderPath, value))
            {
                OnPropertyChanged(nameof(CanNavigateUp));
            }
        }
    }

    public bool CanNavigateUp => ActiveFolderPath.TrimEnd('\\').Length > 2;

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public string PreviewText
    {
        get => _previewText;
        private set => SetProperty(ref _previewText, value);
    }

    public string PreviewMessage
    {
        get => _previewMessage;
        private set => SetProperty(ref _previewMessage, value);
    }

    public bool IsImagePreviewVisible
    {
        get => _isImagePreviewVisible;
        private set => SetProperty(ref _isImagePreviewVisible, value);
    }

    public bool IsTextPreviewVisible
    {
        get => _isTextPreviewVisible;
        private set => SetProperty(ref _isTextPreviewVisible, value);
    }

    public bool IsMessageVisible
    {
        get => _isMessageVisible;
        private set => SetProperty(ref _isMessageVisible, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsNotScanning));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsNotScanning => !IsScanning;

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    /// <summary>Set when a scan could not read the whole master file table, so the results are incomplete.</summary>
    public string ScanWarning
    {
        get => _scanWarning;
        private set
        {
            if (SetProperty(ref _scanWarning, value))
            {
                OnPropertyChanged(nameof(HasScanWarning));
            }
        }
    }

    public bool HasScanWarning => !string.IsNullOrEmpty(ScanWarning);

    public bool IsElevated
    {
        get => _isElevated;
        private set
        {
            if (SetProperty(ref _isElevated, value))
            {
                OnPropertyChanged(nameof(NeedsElevation));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool NeedsElevation => !IsElevated;

    public async Task InitializeAsync()
    {
        RefreshVolumes();
        await Task.CompletedTask;
    }

    public void OnClosing()
    {
        _scanCancellation?.Cancel();
        _contentsCancellation?.Cancel();
        _previewCancellation?.Cancel();
    }

    /// <summary>Double clicking an item in the contents list: folders are opened, files are launched.</summary>
    public void ActivateSelectedContent()
    {
        if (SelectedContent is not { } item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            uint recordNumber = _aggregation is not null && _aggregation.TryGetRecordNumber(item.FullPath, out uint resolved) ? resolved : 0;
            ShowFolder(item.FullPath, recordNumber);
            return;
        }

        ShellService.OpenFile(item.FullPath);
    }

    /// <summary>Opens the folder of the selected result row, whether it matched the rules or not.</summary>
    public void OpenSelectedResultFolder()
    {
        if (SelectedResultNode is { } node)
        {
            ShellService.OpenFolder(node.FullPath);
        }
    }

    private void RefreshVolumes()
    {
        VolumeInfo? previous = SelectedVolume;

        Volumes.Clear();
        foreach (VolumeInfo volume in VolumeEnumerator.GetNtfsVolumes())
        {
            Volumes.Add(volume);
        }

        SelectedVolume = previous is not null
            ? Volumes.FirstOrDefault(volume => volume.DriveLetter == previous.DriveLetter) ?? Volumes.FirstOrDefault()
            : Volumes.FirstOrDefault();

        StatusText = Volumes.Count == 0
            ? "No NTFS volume was found. Connect a drive and press Refresh."
            : $"{Volumes.Count} NTFS volume(s) found.";
    }

    private async Task ScanAsync()
    {
        if (SelectedVolume is not { } volume)
        {
            _dialogs.ShowError("Choose a drive first.");
            return;
        }

        if (!TryBuildSettings(out ScanSettings settings, out string? error))
        {
            _dialogs.ShowError(error ?? "Check the rule values.", "Rules");
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ClearSession();
        Results.Clear();
        RebuildResultTree();
        UpdateResultSummary();
        StatusText = "Opening the volume...";
        ProgressValue = 0;

        var progress = new Progress<ScanProgress>(OnScanProgress);

        try
        {
            ScanReport report = await Task.Run(
                () => new NtfsVolumeScanner().Scan(volume, settings, progress, _scanCancellation.Token),
                _scanCancellation.Token);

            _index = report.Index;
            _aggregation = report.Aggregation;
            _scannedVolume = report.Volume;
            _lastSettings = settings;

            foreach (FolderResult result in report.Results)
            {
                Results.Add(result);
            }

            RebuildResultTree();
            UpdateResultSummary();

            if (report.MftReadCompleted)
            {
                ScanWarning = string.Empty;
                StatusText =
                    $"Scan finished in {report.Elapsed.TotalSeconds:0.0} s - {report.RecordsInUse:N0} records in use, " +
                    $"{report.Index.DirectoryCount:N0} folders indexed.";
            }
            else
            {
                ScanWarning =
                    $"The master file table could only be read to record {report.RecordsRead:N0} of {report.ExpectedRecordCount:N0}, " +
                    "so folders on the rest of the volume were never seen. The list below is incomplete.";
                StatusText = "Scan finished, but the master file table could not be read in full - the results are incomplete.";
            }

            if (!SelectFirstMatchedFolder())
            {
                ShowFolder(string.Empty, 0);
                PreviewMessage = "No folder matched the rules. Raising the size or lowering the file count usually finds something.";
            }

            if (report.Warnings.Count > 0)
            {
                _dialogs.ShowInfo(string.Join(Environment.NewLine + Environment.NewLine, report.Warnings), "Scan finished with notes");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (NtfsScanException exception)
        {
            StatusText = "Scan failed.";
            _dialogs.ShowError(exception.Message, "Scan failed");
        }
        catch (Exception exception)
        {
            StatusText = "Scan failed.";
            _dialogs.ShowError(exception.Message, "Scan failed");
        }
        finally
        {
            IsScanning = false;
            ProgressValue = 0;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private void OnScanProgress(ScanProgress progress)
    {
        ProgressValue = progress.Fraction * 100d;
        StatusText = $"{progress.Stage}: {progress.ItemsProcessed:N0} of {progress.TotalItems:N0} records - {progress.FoldersFound:N0} folders seen.";
    }

    private async Task ImportAsync()
    {
        string? file = _dialogs.OpenTextFile("Import folder list");
        if (file is null)
        {
            return;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = ResultFileFormat.Load(file);
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(exception.Message, "Import failed");
            return;
        }

        if (paths.Count == 0)
        {
            _dialogs.ShowInfo("No folder paths were found in that file.");
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ClearSession();
        Results.Clear();
        RebuildResultTree();
        UpdateResultSummary();
        ProgressValue = 0;
        StatusText = $"Reading {paths.Count:N0} folders...";

        int total = paths.Count;
        var progress = new Progress<int>(done =>
        {
            ProgressValue = total == 0 ? 0 : (double)done / total * 100d;
            StatusText = $"Reading folder {done:N0} of {total:N0}...";
        });

        try
        {
            List<FolderResult> imported = await Task.Run(
                () => MeasureImportedFolders(paths, progress, _scanCancellation.Token),
                _scanCancellation.Token);

            foreach (FolderResult result in imported)
            {
                Results.Add(result);
            }

            RebuildResultTree();
            UpdateResultSummary();
            StatusText = $"Imported {Results.Count:N0} folders from {Path.GetFileName(file)}.";

            SelectFirstMatchedFolder();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Import cancelled.";
        }
        catch (Exception exception)
        {
            StatusText = "Import failed.";
            _dialogs.ShowError(exception.Message, "Import failed");
        }
        finally
        {
            IsScanning = false;
            ProgressValue = 0;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private static List<FolderResult> MeasureImportedFolders(IReadOnlyList<string> paths, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var results = new List<FolderResult>(paths.Count);

        for (int index = 0; index < paths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = paths[index];
            FolderMeasurement measurement = FileSystemListing.Measure(path);

            results.Add(new FolderResult
            {
                FullPath = path,
                RecordNumber = 0,
                DirectFileCount = measurement.DirectFileCount,
                DirectSizeBytes = measurement.DirectSizeBytes,
                TotalSizeBytes = measurement.TotalSizeBytes,
                SizeBytes = measurement.TotalSizeBytes,
                SubfolderCount = measurement.SubfolderCount,
                TotalFileCount = measurement.TotalFileCount,
                SizeIncludesSubfolders = true,
                Exists = measurement.Exists,
            });

            progress?.Report(index + 1);
        }

        results.Sort(static (left, right) => string.Compare(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private void ExportResults()
    {
        if (Results.Count == 0)
        {
            _dialogs.ShowInfo("There is nothing to export yet.");
            return;
        }

        string suggestedName = _scannedVolume is not null
            ? $"folders-{_scannedVolume.DriveLetter}-{DateTime.Now:yyyyMMdd-HHmm}.txt"
            : $"folders-{DateTime.Now:yyyyMMdd-HHmm}.txt";

        string? file = _dialogs.SaveTextFile("Export folder list", suggestedName);
        if (file is null)
        {
            return;
        }

        try
        {
            ResultFileFormat.Save(file, Results, _lastSettings, _scannedVolume);
            StatusText = $"Exported {Results.Count:N0} folders to {file}.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(exception.Message, "Export failed");
        }
    }

    private void ClearResults()
    {
        Results.Clear();
        ClearSession();
        RebuildResultTree();
        UpdateResultSummary();
        ShowFolder(string.Empty, 0);
        PreviewMessage = "Select a folder on the left to see what is inside.";
        StatusText = "Results cleared.";
    }

    private void ClearSession()
    {
        _index = null;
        _aggregation = null;
        _scannedVolume = null;
        ScanWarning = string.Empty;
    }

    private void CancelRunningWork() => _scanCancellation?.Cancel();

    private void ShowFolder(string path, uint recordNumber)
    {
        _contentsCancellation?.Cancel();
        _contentsCancellation?.Dispose();
        _contentsCancellation = null;

        Contents.Clear();
        SelectedContent = null;
        ActiveFolderPath = path ?? string.Empty;
        SetPreviewState(null, null, "Select an item on the right to preview it.");

        if (string.IsNullOrEmpty(ActiveFolderPath))
        {
            return;
        }

        if (_index is not null && _aggregation is not null &&
            _aggregation.TryGetRecordNumber(ActiveFolderPath, out uint resolvedRecord))
        {
            foreach (FileEntry entry in _index.GetChildren(_aggregation, resolvedRecord, ActiveFolderPath))
            {
                Contents.Add(entry);
            }

            StatusText = $"{Contents.Count:N0} items in {ActiveFolderPath}.";
            SelectContentAutomatically();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _contentsCancellation = cancellation;
        _ = LoadContentsFromFileSystemAsync(ActiveFolderPath, cancellation.Token);
    }

    private async Task LoadContentsFromFileSystemAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            StatusText = $"Reading {path}...";
            IReadOnlyList<FileEntry> entries = await Task.Run(() => FileSystemListing.EnumerateChildren(path), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Contents.Clear();
            foreach (FileEntry entry in entries)
            {
                Contents.Add(entry);
            }

            StatusText = $"{entries.Count:N0} items in {path}.";
            SelectContentAutomatically();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PreviewMessage = exception.Message;
        }
    }

    /// <summary>
    /// Selects a file in the folder that was just opened, so the preview pane shows something
    /// without a click. Which file depends on the drop down in the right pane.
    /// </summary>
    private void SelectContentAutomatically() => SelectedContent = AutoSelect.Pick(Contents, AutoSelectMode);

    private void NavigateUp()
    {
        string path = ActiveFolderPath.TrimEnd('\\');
        if (path.Length <= 2)
        {
            return;
        }

        int separator = path.LastIndexOf('\\');
        string parent = separator <= 2 ? path[..2] + "\\" : path[..separator];
        uint recordNumber = _aggregation is not null && _aggregation.TryGetRecordNumber(parent, out uint resolved) ? resolved : 0;
        ShowFolder(parent, recordNumber);
    }

    private void StartPreviewLoad()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;

        FileEntry? item = SelectedContent;
        SetPreviewState(null, null, "Select an item on the right to preview it.");

        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            PreviewMessage = $"Folder - {item.SizeText}, {item.DirectChildCount:N0} items directly inside. Double click to open it here.";
            return;
        }

        if (item.PreviewKind == PreviewKind.None)
        {
            PreviewMessage = $"No preview for this file type. Size: {item.SizeText}.";
            return;
        }

        _ = LoadPreviewAsync(item, cancellation.Token);
    }

    private async Task LoadPreviewAsync(FileEntry item, CancellationToken cancellationToken)
    {
        try
        {
            PreviewResult result = await _previewService.LoadAsync(item, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SetPreviewState(result.Image, result.Text, result.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SetPreviewState(null, null, exception.Message);
        }
    }

    private void SetPreviewState(ImageSource? image, string? text, string? message)
    {
        PreviewImage = image;
        PreviewText = text ?? string.Empty;
        PreviewMessage = message ?? string.Empty;

        IsImagePreviewVisible = image is not null;
        IsTextPreviewVisible = !string.IsNullOrEmpty(text);
        IsMessageVisible = !IsImagePreviewVisible && !IsTextPreviewVisible;
    }

    private void OpenActiveFolder() => ShellService.OpenFolder(ActiveFolderPath);

    private void OpenSelectedContent()
    {
        if (SelectedContent is not { } item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            ShellService.OpenFolder(item.FullPath);
        }
        else
        {
            ShellService.OpenFile(item.FullPath);
        }
    }

    private void CopyActivePath()
    {
        if (ShellService.TryCopyToClipboard(ActiveFolderPath))
        {
            StatusText = "Path copied to the clipboard.";
        }
    }

    private void RelaunchElevated()
    {
        if (ElevationHelper.RelaunchElevated())
        {
            System.Windows.Application.Current?.Shutdown();
        }
        else
        {
            _dialogs.ShowError("The app could not restart with administrator rights.", "Restart failed");
        }
    }

    private void ValidateSettings()
    {
        ValidationMessage = ScanSettings.TryParse(MinSizeText, MinFileCountText, SizeIncludesSubfolders, out _, out string? error)
            ? null
            : error;
    }

    private bool TryBuildSettings(out ScanSettings settings, out string? error) =>
        ScanSettings.TryParse(MinSizeText, MinFileCountText, SizeIncludesSubfolders, out settings, out error);

    /// <summary>
    /// Rebuilds the tree from the folders that pass the filter. A folder that matches keeps the
    /// folders leading to it, so every row stays reachable from its drive.
    /// </summary>
    private void RebuildResultTree()
    {
        foreach (ResultTreeNode node in ResultTree.All(_resultRoots))
        {
            node.PropertyChanged -= OnResultNodeChanged;
        }

        string filter = ResultFilter.Trim();
        IEnumerable<FolderResult> matching = filter.Length == 0
            ? Results
            : Results.Where(result => result.FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase));

        _resultRoots = ResultTree.Build(matching);

        foreach (ResultTreeNode node in ResultTree.All(_resultRoots))
        {
            node.PropertyChanged += OnResultNodeChanged;
        }

        RefreshVisibleResults();
    }

    /// <summary>Refreshes the rows on screen after a folder was expanded or collapsed.</summary>
    private void RefreshVisibleResults() => VisibleResults = ResultTree.Visible(_resultRoots);

    private void OnResultNodeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ResultTreeNode.IsExpanded))
        {
            RefreshVisibleResults();
        }
    }

    private void SetAllExpanded(bool expanded)
    {
        foreach (ResultTreeNode node in ResultTree.All(_resultRoots))
        {
            node.PropertyChanged -= OnResultNodeChanged;
            node.IsExpanded = expanded;
            node.PropertyChanged += OnResultNodeChanged;
        }

        RefreshVisibleResults();
    }

    /// <summary>Selects the first folder in path order that matched the rules.</summary>
    private bool SelectFirstMatchedFolder()
    {
        ResultTreeNode? first = ResultTree.All(_resultRoots).FirstOrDefault(node => node.IsMatch);
        if (first is null)
        {
            return false;
        }

        SelectedResultNode = first;
        return true;
    }

    private void UpdateResultSummary()
    {
        ResultSummary = Results.Count switch
        {
            0 => "No results yet.",
            1 => "1 folder found.",
            _ => $"{Results.Count:N0} folders found.",
        };
    }
}
