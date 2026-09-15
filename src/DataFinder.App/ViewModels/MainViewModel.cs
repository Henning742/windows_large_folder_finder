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
using DataFinder.Core.Preview.Raw;
using DataFinder.Core.Results;
using DataFinder.Core.Util;
using DataFinder.Core.Volumes;

namespace DataFinder.App.ViewModels;

/// <summary>One entry of the "select a file on its own" drop down.</summary>
public sealed record AutoSelectOption(AutoSelectMode Mode, string Text);

public sealed class MainViewModel : ObservableObject
{
    /// <summary>How many pictures each folder shows in the gallery.</summary>
    private const int GalleryPicturesPerFolder = 4;

    /// <summary>How much picture the whole gallery may carry at once.</summary>
    private const long GalleryMaxPictureBytes = 24L * 1024 * 1024;

    private readonly IDialogService _dialogs;
    private readonly PreviewService _previewService = new();
    private readonly RemainingTimeEstimator _importRemaining = new();

    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _contentsCancellation;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _galleryCancellation;

    /// <summary>One report per scanned volume, kept so the preview pane can read the folder tree.</summary>
    private readonly List<ScanReport> _reports = new();

    /// <summary>The folders whose notes were typed or changed since the last export.</summary>
    private readonly HashSet<FolderResult> _unsavedComments = new();

    private string? _importedFrom;
    private ScanSettings? _lastSettings;
    private ScanTimeEstimator? _scanRemaining;
    private System.Diagnostics.Stopwatch? _scanStopwatch;

    private string _scanSummary = string.Empty;
    private string _resultFilter = string.Empty;
    private IReadOnlyList<ResultTreeNode> _resultRoots = Array.Empty<ResultTreeNode>();
    private IReadOnlyList<ResultTreeNode> _visibleResults = Array.Empty<ResultTreeNode>();
    private ResultTreeNode? _selectedResultNode;
    private FileEntry? _selectedContent;
    private AutoSelectMode _autoSelectMode = AutoSelectMode.FirstFile;
    private RightPaneMode _rightPane = RightPaneMode.Contents;
    private string _gallerySummary = "Nothing is on show yet.";
    private string _selectedComment = string.Empty;
    private string _activeFolderPath = string.Empty;
    private ImageSource? _previewImage;
    private string _previewText = string.Empty;
    private string _previewMessage = "Select a folder on the left to see what is inside.";
    private IReadOnlyList<RawSchema> _previewSchemas = Array.Empty<RawSchema>();
    private RawSchema? _selectedPreviewSchema;
    /// <summary>The suffixes the folder on screen was listed with, so it is only listed again when
    /// that list really changed - and not on every keystroke in the dialog.</summary>
    private IReadOnlyList<string> _appliedSuffixes = RawFileTypes.Default;
    private bool _showAllSchematics;
    private bool _stretchPreview;
    private bool _canStretchPreview;
    private bool _isTilePreviewVisible;
    private bool _isImagePreviewVisible;
    private bool _isTextPreviewVisible;
    private bool _isMessageVisible = true;
    private bool _isScanning;
    private bool _isElevated;
    private double _progressValue;
    private string _remainingText = string.Empty;
    private string _statusText = "Ready. Choose a drive and press Scan.";
    private string _resultSummary = "No results yet.";
    private string _scanWarning = string.Empty;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        ScanSetup = new ScanDialogViewModel();
        DecodeSetup = new DecodeDialogViewModel();
        DecodeSetup.Changed += OnDecodeSettingsChanged;
        IsElevated = ElevationHelper.IsElevated();
        RebuildPreviewSchemas();
        ResetStretchToDefault();

        CancelCommand = new RelayCommand(CancelRunningWork, () => IsScanning);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsScanning);
        ExportCommand = new RelayCommand(ExportResults, () => Results.Count > 0);
        ClearResultsCommand = new RelayCommand(ClearResults, () => Results.Count > 0 && !IsScanning);
        ExportWebPageCommand = new AsyncRelayCommand(ExportWebPageAsync, () => Results.Count > 0 && !IsScanning);
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true), () => VisibleResults.Count > 0);
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false), () => VisibleResults.Count > 0);
        OpenFolderCommand = new RelayCommand(OpenActiveFolder, () => ActiveFolderPath.Length > 0);
        OpenContentCommand = new RelayCommand(OpenSelectedContent, () => SelectedContent is not null);
        CopyPathCommand = new RelayCommand(CopyActivePath, () => ActiveFolderPath.Length > 0);
        NavigateUpCommand = new RelayCommand(NavigateUp, () => CanNavigateUp);
        RelaunchElevatedCommand = new RelayCommand(RelaunchElevated, () => !IsElevated);
        RefreshGalleryCommand = new RelayCommand(() => StartGalleryLoad(), () => IsGalleryVisible && Results.Count > 0 && !IsScanning);
        ShowInTreeCommand = new RelayCommand(
            parameter => ShowGalleryFolderInTree(parameter as GalleryFolder),
            parameter => parameter is GalleryFolder);
    }

    /// <summary>
    /// Says that a row of the result list is the one to look at. The list scrolls to it, which the
    /// view model cannot do itself.
    /// </summary>
    public event EventHandler<ResultTreeNode>? ResultRowRevealed;

    /// <summary>
    /// The drives and rules the *Scan...* dialog edits. It is kept here so the choices survive the
    /// dialog being closed.
    /// </summary>
    public ScanDialogViewModel ScanSetup { get; }

    /// <summary>
    /// The file suffixes and the data schematics the preview uses. It is kept here so the choices
    /// survive the dialog being closed, and it is the same object the dialog edits.
    /// </summary>
    public DecodeDialogViewModel DecodeSetup { get; }

    public ObservableCollection<FolderResult> Results { get; } = new();

    public ObservableCollection<FileEntry> Contents { get; } = new();

    /// <summary>The pictures of the selected data file, one per schematic that was asked for.</summary>
    public ObservableCollection<DecodeTile> PreviewTiles { get; } = new();

    /// <summary>
    /// The folders of the whole list, each with a few pictures of what is inside it. It is filled
    /// while the right pane is showing it, and emptied as soon as the list it came from changes.
    /// </summary>
    public ObservableCollection<GalleryFolder> GalleryFolders { get; } = new();

    /// <summary>The choices of the "select a file on its own" drop down, in the order they are shown.</summary>
    public IReadOnlyList<AutoSelectOption> AutoSelectOptions { get; } = new[]
    {
        new AutoSelectOption(AutoSelectMode.FirstFile, "First file"),
        new AutoSelectOption(AutoSelectMode.MiddleFile, "Middle file"),
        new AutoSelectOption(AutoSelectMode.RandomFile, "Random file"),
        new AutoSelectOption(AutoSelectMode.None, "Nothing"),
    };

    /// <summary>The choices of the "what the right pane shows" drop down, in the order they are shown.</summary>
    public IReadOnlyList<RightPaneOption> RightPaneOptions { get; } = new[]
    {
        new RightPaneOption(RightPaneMode.Contents, "The selected folder"),
        new RightPaneOption(RightPaneMode.AllFolders, "Every folder, with pictures"),
    };

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand ClearResultsCommand { get; }

    /// <summary>Writes the whole list out as a web page, pictures and all.</summary>
    public AsyncRelayCommand ExportWebPageCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public RelayCommand OpenContentCommand { get; }

    public RelayCommand CopyPathCommand { get; }

    public RelayCommand NavigateUpCommand { get; }

    public RelayCommand RelaunchElevatedCommand { get; }

    /// <summary>Picks a fresh set of pictures for every folder on show in the gallery.</summary>
    public RelayCommand RefreshGalleryCommand { get; }

    /// <summary>Picks a folder of the gallery in the result list, and goes back to the contents pane.</summary>
    public RelayCommand ShowInTreeCommand { get; }

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
            if (!SetProperty(ref _selectedResultNode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedFolder));
            SelectedComment = value?.Result?.Comment ?? string.Empty;

            if (value is not null)
            {
                ShowFolder(value.FullPath);
            }
        }
    }

    /// <summary>True when the selected row stands for a folder that matched the rules.</summary>
    public bool HasSelectedFolder => SelectedResultNode?.Result is not null;

    /// <summary>
    /// The note that belongs to the selected folder. It is written to the comment column of the CSV
    /// when the report is exported, and read back from there when a report is imported.
    /// </summary>
    public string SelectedComment
    {
        get => _selectedComment;
        set
        {
            if (!SetProperty(ref _selectedComment, value))
            {
                return;
            }

            // The note lives on the folder, and the tree row shows it: the node forwards the change.
            // Only a real change counts as unsaved; picking another row also lands here.
            if (SelectedResultNode is { Result: not null } node &&
                !string.Equals(node.Comment, value, StringComparison.Ordinal))
            {
                node.Comment = value;
                MarkCommentChanged(node.Result);
            }
        }
    }

    /// <summary>True when notes have been typed or changed that the report does not have yet.</summary>
    public bool HasUnsavedComments => _unsavedComments.Count > 0;

    /// <summary>
    /// The window title says so too, because the note is easy to miss: the app keeps a marker in the
    /// title bar while there are notes that no export has picked up.
    /// </summary>
    public string WindowTitle => _unsavedComments.Count switch
    {
        0 => "NTFS Folder Finder",
        1 => "NTFS Folder Finder - 1 comment not saved",
        _ => $"NTFS Folder Finder - {_unsavedComments.Count} comments not saved",
    };

    public FileEntry? SelectedContent
    {
        get => _selectedContent;
        set
        {
            if (SetProperty(ref _selectedContent, value))
            {
                ResetStretchToDefault();
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

    /// <summary>
    /// What the right pane shows: the contents of the folder picked on the left, or every folder of
    /// the list at once with a few pictures of what is inside each of them.
    /// </summary>
    public RightPaneMode RightPane
    {
        get => _rightPane;
        set
        {
            if (!SetProperty(ref _rightPane, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsContentsPaneVisible));
            OnPropertyChanged(nameof(IsGalleryVisible));

            if (IsGalleryVisible)
            {
                StartGalleryLoad();
            }
            else
            {
                CancelGalleryLoad();
            }
        }
    }

    /// <summary>True while the pane shows the contents of one folder.</summary>
    public bool IsContentsPaneVisible => _rightPane == RightPaneMode.Contents;

    /// <summary>True while the pane shows every folder of the list, pictures and all.</summary>
    public bool IsGalleryVisible => _rightPane == RightPaneMode.AllFolders;

    /// <summary>What there is to say about the gallery: how far it has got, and what it ended up with.</summary>
    public string GallerySummary
    {
        get => _gallerySummary;
        private set => SetProperty(ref _gallerySummary, value);
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

    /// <summary>The schematics the preview offers, in the order the dialog lists them.</summary>
    public IReadOnlyList<RawSchema> PreviewSchemas
    {
        get => _previewSchemas;
        private set => SetProperty(ref _previewSchemas, value);
    }

    /// <summary>The schematic a data file is read with. The first one is used until picked otherwise.</summary>
    public RawSchema? SelectedPreviewSchema
    {
        get => _selectedPreviewSchema;
        set
        {
            if (!SetProperty(ref _selectedPreviewSchema, value))
            {
                return;
            }

            if (SelectedContent is { PreviewKind: PreviewKind.Binary })
            {
                ResetStretchToDefault();
                StartPreviewLoad();
            }
        }
    }

    /// <summary>
    /// True when a data file is read with every ticked schematic at once and the pictures are drawn
    /// side by side, which saves picking the right one by trial and error.
    /// </summary>
    public bool ShowAllSchematics
    {
        get => _showAllSchematics;
        set
        {
            if (!SetProperty(ref _showAllSchematics, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSingleSchematicMode));
            OnPropertyChanged(nameof(AllSchematicsSummary));

            if (SelectedContent is { PreviewKind: PreviewKind.Binary })
            {
                ResetStretchToDefault();
                StartPreviewLoad();
            }
        }
    }

    /// <summary>True while the drop down of single schematics is the one that decides.</summary>
    public bool IsSingleSchematicMode => !ShowAllSchematics;

    /// <summary>How many schematics are drawn side by side, shown next to the tick.</summary>
    public string AllSchematicsSummary => DecodeSetup.ShownSchemas.Count switch
    {
        0 => "no schematic is ticked",
        1 => "1 schematic",
        var count => $"{count} schematics",
    };

    /// <summary>
    /// True while the frames on show are stretched to the full range. It is a look rather than a
    /// setting: it starts at what the schematic's layout calls for - 16 bit data stretched so it
    /// can be seen at all, 8 bit data as it is - and it goes back to that whenever another file or
    /// another schematic is picked.
    /// </summary>
    public bool StretchPreview
    {
        get => _stretchPreview;
        set
        {
            if (!SetProperty(ref _stretchPreview, value))
            {
                return;
            }

            if (SelectedContent is { PreviewKind: PreviewKind.Binary })
            {
                StartPreviewLoad();
            }
        }
    }

    /// <summary>False while nothing on show has a grey level to stretch, which is the case for colour.</summary>
    public bool CanStretchPreview
    {
        get => _canStretchPreview;
        private set => SetProperty(ref _canStretchPreview, value);
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

    /// <summary>True while decoded pictures are on show instead of the plain image or text preview.</summary>
    public bool IsTilePreviewVisible
    {
        get => _isTilePreviewVisible;
        private set => SetProperty(ref _isTilePreviewVisible, value);
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

    /// <summary>
    /// How much longer the running scan is going to take, in words. Empty when nothing is running,
    /// and a short "estimating" note during the first moments of a scan, when the rate says nothing.
    /// </summary>
    public string RemainingText
    {
        get => _remainingText;
        private set => SetProperty(ref _remainingText, value);
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

    /// <summary>
    /// The drives and the rules the results below were found with, shown in the status bar. The
    /// *Scan...* dialog is the only place where they can be changed.
    /// </summary>
    public string ScanSummary
    {
        get => _scanSummary;
        private set => SetProperty(ref _scanSummary, value);
    }

    public bool IsElevated
    {
        get => _isElevated;
        private set
        {
            if (SetProperty(ref _isElevated, value))
            {
                ScanSetup.IsElevated = value;
                OnPropertyChanged(nameof(NeedsElevation));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool NeedsElevation => !IsElevated;

    public async Task InitializeAsync()
    {
        ScanSetup.IsElevated = IsElevated;
        ScanSetup.RefreshVolumes();

        StatusText = ScanSetup.VolumeChoices.Count == 0
            ? "No NTFS volume was found. Connect a drive and open Scan..."
            : "Ready. Press Scan... to choose the drives and the rules.";

        await Task.CompletedTask;
    }

    public void OnClosing()
    {
        _scanCancellation?.Cancel();
        _contentsCancellation?.Cancel();
        _previewCancellation?.Cancel();
        CancelGalleryLoad();
    }

    /// <summary>True when the window may close. False when the user wants to keep their notes.</summary>
    public bool CanClose() =>
        ConfirmDiscardingComments("Closing the app throws them away.");

    /// <summary>Double clicking an item in the contents list: folders are opened, files are launched.</summary>
    public void ActivateSelectedContent()
    {
        if (SelectedContent is not { } item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            ShowFolder(item.FullPath);
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

    /// <summary>
    /// Starts the run the *Scan...* dialog was set up for. The dialog is already closed by the time
    /// this is called, so every setting is read once, up front.
    /// </summary>
    public async Task StartScanAsync()
    {
        IReadOnlyList<VolumeInfo> volumes = ScanSetup.SelectedVolumes;
        if (volumes.Count == 0)
        {
            _dialogs.ShowError("Choose a drive first.");
            return;
        }

        if (!ScanSetup.TryBuildSettings(out ScanSettings settings, out string? error))
        {
            _dialogs.ShowError(error ?? "Check the rule values.", "Rules");
            return;
        }

        if (!ConfirmDiscardingComments("Starting a new scan replaces the list, and the notes go with it."))
        {
            return;
        }

        ScanSummary = ScanSetup.DescribeSelection();

        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ClearSession();
        _lastSettings = settings;
        Results.Clear();
        ClearGallery();
        MarkCommentsSaved();
        RebuildResultTree();
        UpdateResultSummary();
        ProgressValue = 0;

        ScanWarning = string.Empty;
        var warnings = new List<string>();
        var incomplete = new List<VolumeInfo>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var scanner = new NtfsVolumeScanner();

        _scanStopwatch = stopwatch;
        _scanRemaining = new ScanTimeEstimator(volumes);
        RemainingText = "Estimating how long this will take...";

        try
        {
            for (int index = 0; index < volumes.Count; index++)
            {
                VolumeInfo volume = volumes[index];
                int position = index + 1;
                StatusText = $"Opening {volume.DriveLetter}: ({position} of {volumes.Count})...";
                ProgressValue = (double)index / volumes.Count * 100d;

                // The report is kept as soon as a volume is done, so the folders of a finished drive
                // can already be opened while the next one is being read.
                var progress = new Progress<ScanProgress>(update => OnScanProgress(update, volume, index, volumes.Count));
                ScanReport report = await Task.Run(
                    () => scanner.Scan(volume, settings, progress, _scanCancellation.Token),
                    _scanCancellation.Token);

                _reports.Add(report);
                _importedFrom = null;

                foreach (FolderResult result in report.Results)
                {
                    Results.Add(result);
                }

                RebuildResultTree();
                UpdateResultSummary();

                if (!report.MftReadCompleted)
                {
                    incomplete.Add(volume);
                }

                warnings.AddRange(report.Warnings.Select(warning => $"{volume.DriveLetter}: {warning}"));
            }

            DescribeScanResult(stopwatch.Elapsed, warnings, incomplete);

            if (!SelectFirstMatchedFolder())
            {
                ShowFolder(string.Empty);
                PreviewMessage = "No folder matched the rules. Raising the size or lowering the file count usually finds something.";
            }

            if (warnings.Count > 0)
            {
                _dialogs.ShowInfo(string.Join(Environment.NewLine + Environment.NewLine, warnings), "Scan finished with notes");
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
            RemainingText = string.Empty;
            _scanRemaining = null;
            _scanStopwatch = null;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            RefreshGalleryIfShown();
        }
    }

    /// <summary>Says how the scan went once every selected drive has been read.</summary>
    private void DescribeScanResult(TimeSpan elapsed, IReadOnlyList<string> warnings, IReadOnlyList<VolumeInfo> incomplete)
    {
        long recordsInUse = _reports.Sum(report => report.RecordsInUse);
        int folders = _reports.Sum(report => report.Index.DirectoryCount);
        string drives = _reports.Count == 1 ? "1 drive" : $"{_reports.Count} drives";

        if (incomplete.Count == 0)
        {
            // A table read from end to end can still have something to report: records that could not
            // be followed, files whose name was not readable. That is worth saying too - it is just
            // not the same as a list that is known to be missing whole folders.
            ScanWarning = Headline(warnings);
            StatusText =
                $"Scan finished in {elapsed.TotalSeconds:0.0} s - {recordsInUse:N0} records in use, " +
                $"{folders:N0} folders indexed on {drives}.";
            return;
        }

        string headline = Headline(warnings);
        ScanWarning =
            $"On {string.Join(", ", incomplete.Select(volume => volume.DriveLetter + ":"))} the master file table " +
            "could not be read in full, so folders on the rest of those volumes were never seen. The list is incomplete." +
            (headline.Length > 0 ? $" {headline}" : string.Empty);
        StatusText = "Scan finished, but at least one master file table could not be read in full - the results are incomplete.";
    }

    /// <summary>
    /// The one line to show about what a scan had to report, and how many more things there were:
    /// the whole list is in the report file next to the CSV, which is where a reader who wants the
    /// detail will look anyway.
    /// </summary>
    private static string Headline(IReadOnlyList<string> warnings) => warnings.Count switch
    {
        0 => string.Empty,
        1 => warnings[0],
        _ => $"{warnings[0]} ({warnings.Count - 1} more in the report)",
    };

    private void OnScanProgress(ScanProgress progress, VolumeInfo volume, int volumeIndex, int volumeCount)
    {
        // Progress reports are posted to the UI thread, so one can still arrive after the scan it
        // belongs to has finished. By then there is nothing left to say.
        if (!IsScanning || _scanRemaining is null || _scanStopwatch is null)
        {
            return;
        }

        // Each drive fills its own share of the bar, and how long the whole run will take is worked
        // out from the share that is done.
        TimeSpan? remaining = _scanRemaining.Update(volumeIndex, progress.Fraction, _scanStopwatch.Elapsed);

        ProgressValue = _scanRemaining.Fraction(volumeIndex, progress.Fraction) * 100d;
        RemainingText = DescribeRemaining(remaining);
        StatusText =
            $"{volume.DriveLetter}: {progress.Stage}: {progress.ItemsProcessed:N0} of {progress.TotalItems:N0} records - " +
            $"{progress.FoldersFound:N0} folders seen.";
    }

    /// <summary>The estimate in words, so the status bar can show it next to the progress bar.</summary>
    private static string DescribeRemaining(TimeSpan? remaining) =>
        remaining is null
            ? "Estimating how long this will take..."
            : $"About {DurationText.Format(remaining.Value)} left";

    private async Task ImportAsync()
    {
        if (!ConfirmDiscardingComments("Importing replaces the list, and the notes go with it."))
        {
            return;
        }

        string? file = _dialogs.OpenReportFile("Import folder list");
        if (file is null)
        {
            return;
        }

        IReadOnlyList<ResultRow> rows;
        try
        {
            // Reports are CSV now; the older text format is still read so old lists keep working.
            rows = string.Equals(Path.GetExtension(file), ".csv", StringComparison.OrdinalIgnoreCase)
                ? CsvResultFormat.Load(file)
                : ResultFileFormat.LoadRows(file);
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(exception.Message, "Import failed");
            return;
        }

        if (rows.Count == 0)
        {
            _dialogs.ShowInfo("No folder paths were found in that file.");
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ClearSession();
        _importedFrom = file;

        // Imported folders were not picked by the rules, so no rule values belong in the report
        // this list is exported as.
        _lastSettings = null;
        Results.Clear();
        ClearGallery();
        MarkCommentsSaved();
        RebuildResultTree();
        UpdateResultSummary();
        ProgressValue = 0;
        StatusText = $"Reading {rows.Count:N0} folders...";

        int total = rows.Count;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _importRemaining.Reset();
        RemainingText = "Estimating how long this will take...";

        var progress = new Progress<int>(done =>
        {
            if (!IsScanning)
            {
                return;
            }

            double fraction = total == 0 ? 0d : (double)done / total;
            ProgressValue = fraction * 100d;
            RemainingText = DescribeRemaining(_importRemaining.Update(fraction, stopwatch.Elapsed));
            StatusText = $"Reading folder {done:N0} of {total:N0}...";
        });

        try
        {
            List<FolderResult> imported = await Task.Run(
                () => MeasureImportedFolders(rows, progress, _scanCancellation.Token),
                _scanCancellation.Token);

            foreach (FolderResult result in imported)
            {
                Results.Add(result);
            }

            RebuildResultTree();
            UpdateResultSummary();
            StatusText = $"Imported {Results.Count:N0} folders from {Path.GetFileName(file)}.";
            ScanSummary = $"read from {Path.GetFileName(file)}";

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
            RemainingText = string.Empty;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            RefreshGalleryIfShown();
        }
    }

    private static List<FolderResult> MeasureImportedFolders(IReadOnlyList<ResultRow> rows, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var results = new List<FolderResult>(rows.Count);

        for (int index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A report the app wrote carries every number, and there the row is described from the
            // report; a list of bare paths is measured from the file system instead.
            results.Add(ImportedFolder.Describe(rows[index]));
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

        string? file = _dialogs.SaveReportFile("Export folder list", SuggestedReportName(".csv"));
        if (file is null)
        {
            return;
        }

        try
        {
            ReportMetadata metadata = CsvResultFormat.BuildMetadata(
                _reports.Select(report => report.Volume),
                _lastSettings,
                _reports,
                imported: _reports.Count == 0,
                importedFrom: _importedFrom);

            CsvResultFormat.Save(file, Results, metadata);
            MarkCommentsSaved();

            StatusText =
                $"Exported {Results.Count:N0} folders to {file}, with the notes about the report in " +
                $"{CsvResultFormat.MetadataPathFor(file)}.";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(exception.Message, "Export failed");
        }
    }

    private void MarkCommentChanged(FolderResult folder)
    {
        if (_unsavedComments.Add(folder))
        {
            OnPropertyChanged(nameof(HasUnsavedComments));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>The notes are in the report now, or the list they belonged to is gone.</summary>
    private void MarkCommentsSaved()
    {
        if (_unsavedComments.Count == 0)
        {
            return;
        }

        _unsavedComments.Clear();
        OnPropertyChanged(nameof(HasUnsavedComments));
        OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>
    /// Asks before notes that were never exported are thrown away. <paramref name="whatHappens"/>
    /// finishes the sentence "…and the notes go with it".
    /// </summary>
    private bool ConfirmDiscardingComments(string whatHappens)
    {
        if (!HasUnsavedComments)
        {
            return true;
        }

        string notes = _unsavedComments.Count == 1
            ? "1 folder has a note that has not been exported yet."
            : $"{_unsavedComments.Count} folders have notes that have not been exported yet.";

        return _dialogs.Confirm($"{notes}\n\n{whatHappens}\n\nContinue?", "Comments not saved");
    }

    /// <summary>
    /// Writes every folder in the list out as one web page, with a few pictures of what is inside
    /// each of them. The page is built from the same tree the left pane shows, so it reads the same
    /// way, and the notes typed here travel with it.
    /// </summary>
    private async Task ExportWebPageAsync()
    {
        if (Results.Count == 0)
        {
            _dialogs.ShowInfo("There is nothing to export yet.");
            return;
        }

        string? file = _dialogs.SaveWebPageFile("Export the list as a web page", SuggestedReportName(".html"));
        if (file is null)
        {
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        ProgressValue = 0;
        RemainingText = "Estimating how long this will take...";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var remaining = new RemainingTimeEstimator();

        var progress = new Progress<HtmlReportProgress>(step =>
        {
            if (!IsScanning)
            {
                return;
            }

            double fraction = step.FoldersTotal == 0 ? 1d : (double)step.FoldersDone / step.FoldersTotal;
            ProgressValue = fraction * 100d;
            RemainingText = DescribeRemaining(remaining.Update(fraction, stopwatch.Elapsed));
            StatusText = $"Looking inside {step.Message}...";
        });

        try
        {
            // The page carries every folder in the list, not only the rows the filter is showing.
            IReadOnlyList<ResultTreeNode> tree = ResultTree.Build(Results);
            IReadOnlyList<string> suffixes = DecodeSetup.Extensions;
            IReadOnlyList<RawSchema> schemas = DecodeSetup.ShownSchemas.Count > 0
                ? DecodeSetup.ShownSchemas
                : DecodeSetup.UsableSchemas;
            bool stretch = StretchPreview;
            CancellationToken token = _scanCancellation.Token;

            var options = new HtmlReportOptions
            {
                Title = "Folders found",
                Source = ScanSummary.Length > 0 ? ScanSummary : null,
                Warning = ScanWarning.Length > 0 ? ScanWarning : null,
            };

            var builder = new HtmlReportBuilder();

            // The pictures are written into a folder beside the page rather than into the page, so a
            // report over hundreds of folders stays small enough for a browser to open.
            string pictureFolder = Path.GetFileNameWithoutExtension(file) + ".files";
            HtmlReportBuildResult result = await Task.Run(
                () => builder.Build(
                    tree,
                    ListFilesForPage,
                    suffixes,
                    schemas,
                    stretch,
                    DateTimeOffset.Now,
                    options,
                    progress,
                    token,
                    pictureFolder),
                token);

            HtmlReport.WritePictures(file, result.Files);
            HtmlReport.SaveText(file, result.Html);

            StatusText = result.Pictures == 0
                ? $"Wrote {result.Folders:N0} folders to {Path.GetFileName(file)}."
                : $"Wrote {result.Folders:N0} folders to {Path.GetFileName(file)}, " +
                  $"with {result.Pictures:N0} pictures in the {pictureFolder} folder beside it.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Writing the web page was cancelled.";
        }
        catch (Exception exception)
        {
            StatusText = "Writing the web page failed.";
            _dialogs.ShowError(exception.Message, "Export failed");
        }
        finally
        {
            IsScanning = false;
            ProgressValue = 0;
            RemainingText = string.Empty;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    /// <summary>
    /// Fills the gallery: every folder of the list at once, each with a few pictures of what is
    /// inside it, picked at random. It is the same work the web page does, and it reads the folders
    /// the same way, so what the window shows and what the page shows are the same thing.
    /// </summary>
    private void StartGalleryLoad()
    {
        CancelGalleryLoad();
        GalleryFolders.Clear();

        if (Results.Count == 0)
        {
            GallerySummary = "There is nothing in the list yet. Scan a drive or import a report first.";
            return;
        }

        if (IsScanning)
        {
            // A scan or an import is still reading the list, so the gallery waits for it rather than
            // gathering half of a list that is about to be replaced.
            GallerySummary = "The list is still being read; the pictures are gathered as soon as it is done.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _galleryCancellation = cancellation;
        _ = LoadGalleryAsync(cancellation);
    }

    /// <summary>
    /// Stops a gathering run that is under way. What it had gathered is either replaced by the next
    /// run or left on the screen; either way the window stops saying it is busy.
    /// </summary>
    private void CancelGalleryLoad()
    {
        CancellationTokenSource? cancellation = _galleryCancellation;
        _galleryCancellation = null;

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();

        // The busy state belongs to whatever else is running too - a scan, an import or the web page
        // - so only the gallery's own share of it is given up here.
        if (_scanCancellation is null)
        {
            IsScanning = false;
            ProgressValue = 0;
            RemainingText = string.Empty;
        }
    }

    /// <summary>
    /// Empties the gallery, which is what the list it was drawn from changing means for it. While
    /// it is not the pane on show there is no reason to gather it again: it is gathered when it is
    /// asked for.
    /// </summary>
    private void ClearGallery()
    {
        CancelGalleryLoad();
        GalleryFolders.Clear();
    }

    /// <summary>
    /// Gathers the gallery again after the list it was drawn from has changed. While the gallery is
    /// not the pane on show there is nothing to gather for: it is gathered when it is asked for.
    /// </summary>
    private void RefreshGalleryIfShown()
    {
        if (IsGalleryVisible)
        {
            StartGalleryLoad();
        }
    }

    private async Task LoadGalleryAsync(CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        IsScanning = true;
        ProgressValue = 0;
        RemainingText = "Estimating how long this will take...";
        GallerySummary = "Gathering the pictures...";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var remaining = new RemainingTimeEstimator();

        // Each folder goes on the screen as soon as it is done, so a long list fills while the run
        // is still going instead of leaving the pane empty until the end.
        int gathered = 0;
        var progress = new Progress<GalleryStep>(step =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            GalleryFolders.Add(step.Folder);
            gathered += step.Folder.Pictures.Count;

            double fraction = step.FoldersTotal == 0 ? 1d : (double)step.FoldersDone / step.FoldersTotal;
            ProgressValue = fraction * 100d;
            RemainingText = DescribeRemaining(remaining.Update(fraction, stopwatch.Elapsed));
            StatusText = $"Looking inside {step.Folder.FullPath}...";
            GallerySummary = $"{step.FoldersDone:N0} of {step.FoldersTotal:N0} folders, {gathered:N0} pictures so far.";
        });

        try
        {
            // The gallery carries every folder of the list, not only the rows the filter is showing.
            List<ResultTreeNode> folders = ResultTree.All(ResultTree.Build(Results))
                .Where(node => node.IsMatch)
                .ToList();

            IReadOnlyList<string> suffixes = DecodeSetup.Extensions;
            IReadOnlyList<RawSchema> schemas = DecodeSetup.ShownSchemas.Count > 0
                ? DecodeSetup.ShownSchemas
                : DecodeSetup.UsableSchemas;
            bool stretch = StretchPreview;

            GalleryTotals totals = await Task.Run(
                () => GatherGallery(folders, suffixes, schemas, stretch, progress, token),
                token);

            if (!token.IsCancellationRequested)
            {
                GallerySummary = DescribeGallery(folders.Count, totals);
                StatusText = totals.Pictures == 0
                    ? $"Looked inside {FolderCount(folders.Count)}; nothing there could be shown as a picture."
                    : $"Gathered {totals.Pictures:N0} pictures from {FolderCount(folders.Count)}.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            GallerySummary = exception.Message;
            StatusText = "Gathering the pictures failed.";
        }
        finally
        {
            // Only the run that is still the current one clears the busy state: a run that was
            // replaced left that to the one that replaced it.
            if (ReferenceEquals(_galleryCancellation, cancellation))
            {
                _galleryCancellation = null;

                if (_scanCancellation is null)
                {
                    IsScanning = false;
                    ProgressValue = 0;
                    RemainingText = string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// Walks the folders, taking a few pictures out of each. It runs off the UI thread and hands
    /// each folder over as soon as it is done, so the gallery fills while the run is still going.
    /// </summary>
    private GalleryTotals GatherGallery(
        IReadOnlyList<ResultTreeNode> folders,
        IReadOnlyList<string> decodeSuffixes,
        IReadOnlyList<RawSchema> schemas,
        bool stretch,
        IProgress<GalleryStep> progress,
        CancellationToken cancellationToken)
    {
        var finder = new FolderPictureFinder(new HtmlReportLimits
        {
            PicturesPerFolder = GalleryPicturesPerFolder,
            MaxTotalPictureBytes = GalleryMaxPictureBytes,
        });

        for (int index = 0; index < folders.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ResultTreeNode node = folders[index];

            FolderPictures found = node.Result is { Exists: false }
                ? new FolderPictures(
                    Array.Empty<FolderPreviewPicture>(),
                    "The folder is not there any more, so there is nothing to show.")
                : finder.Find(node.FullPath, ListFilesForPage, decodeSuffixes, schemas, stretch, cancellationToken);

            var pictures = new List<GalleryPicture>(found.Pictures.Count);
            foreach (FolderPreviewPicture picture in found.Pictures)
            {
                pictures.Add(new GalleryPicture(
                    picture.Caption,
                    picture.Note,
                    Path.Combine(node.FullPath, picture.Caption),
                    picture.Bytes));
            }

            progress.Report(new GalleryStep(DescribeFolder(node, pictures, found.Note), index + 1, folders.Count));
        }

        return new GalleryTotals(finder.Pictures, finder.LeftOut);
    }

    /// <summary>One card of the gallery: the folder, what is in it, and the pictures that came out of it.</summary>
    private static GalleryFolder DescribeFolder(
        ResultTreeNode node,
        IReadOnlyList<GalleryPicture> pictures,
        string? note) => new()
    {
        Name = node.Name,
        FullPath = node.FullPath,
        Summary = node.Result?.DetailText ?? string.Empty,
        Comment = node.Comment,
        Note = note,
        Pictures = pictures,
    };

    /// <summary>What the gallery says once it has the lot: how many folders, and how many pictures.</summary>
    private static string DescribeGallery(int folders, GalleryTotals totals)
    {
        string leftOut = totals.LeftOut switch
        {
            0 => string.Empty,
            1 => " 1 picture was left out because the gallery was full.",
            _ => $" {totals.LeftOut:N0} pictures were left out because the gallery was full.",
        };

        string inside = folders == 1 ? "it" : "them";

        return totals.Pictures == 0
            ? $"{FolderCount(folders)}, with nothing inside that could be shown as a picture.{leftOut}"
            : $"{FolderCount(folders)}, with {totals.Pictures:N0} pictures of what is inside {inside}, picked at random.{leftOut}";
    }

    private static string FolderCount(int folders) => folders == 1 ? "1 folder" : $"{folders:N0} folders";

    /// <summary>Picks a folder of the gallery in the result list, and goes back to the contents pane.</summary>
    private void ShowGalleryFolderInTree(GalleryFolder? folder)
    {
        if (folder is null)
        {
            return;
        }

        ResultTreeNode? node = ResultTree.All(_resultRoots)
            .FirstOrDefault(candidate => string.Equals(candidate.FullPath, folder.FullPath, StringComparison.OrdinalIgnoreCase));

        if (node is null)
        {
            return;
        }

        RightPane = RightPaneMode.Contents;
        ExpandTheWayTo(_resultRoots, node.FullPath);
        SelectedResultNode = node;
        ResultRowRevealed?.Invoke(this, node);
    }

    /// <summary>
    /// Opens every folder on the way to a row, so that the row itself is part of the list on
    /// screen and can be picked. Returns false when the row is not in the tree at all.
    /// </summary>
    private static bool ExpandTheWayTo(IReadOnlyList<ResultTreeNode> nodes, string path)
    {
        foreach (ResultTreeNode node in nodes)
        {
            if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ExpandTheWayTo(node.Children, path))
            {
                node.IsExpanded = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>How far the gallery has got, and the folder it has just finished.</summary>
    private sealed record GalleryStep(GalleryFolder Folder, int FoldersDone, int FoldersTotal);

    /// <summary>What the whole gallery ended up holding.</summary>
    private sealed record GalleryTotals(int Pictures, int LeftOut);

    /// <summary>
    /// What sits inside a folder, for the web page: the scan that is still in memory answers when
    /// it can, and the file system answers for imported folders and for anything else. The scan
    /// index costs nothing to ask, which is what keeps a big report quick.
    /// </summary>
    private IReadOnlyList<FileEntry> ListFilesForPage(string path)
    {
        ScanReport? report = ReportFor(path);
        if (report is not null && report.Aggregation.TryGetRecordNumber(path, out uint record))
        {
            return report.Index.GetChildren(report.Aggregation, record, path);
        }

        return FileSystemListing.EnumerateChildren(path, DecodeSetup.Extensions);
    }

    /// <summary>A file name that says where the folders came from and when they were found.</summary>
    private string SuggestedReportName(string extension)
    {
        string source = _reports.Count switch
        {
            0 => "imported",
            1 => _reports[0].Volume.DriveLetter.ToString(),
            _ => $"{_reports.Count}drives",
        };

        return $"folders-{source}-{DateTime.Now:yyyyMMdd-HHmm}{extension}";
    }

    private void ClearResults()
    {
        if (!ConfirmDiscardingComments("Clearing the list throws them away."))
        {
            return;
        }

        Results.Clear();
        ClearSession();
        ClearGallery();
        MarkCommentsSaved();
        ScanSummary = string.Empty;
        RebuildResultTree();
        UpdateResultSummary();
        ShowFolder(string.Empty);
        PreviewMessage = "Select a folder on the left to see what is inside.";
        RefreshGalleryIfShown();
        StatusText = "Results cleared.";
    }

    private void ClearSession()
    {
        _reports.Clear();
        _importedFrom = null;
        ScanWarning = string.Empty;
    }

    /// <summary>
    /// The scan that covered a folder, so the preview pane can use the folder tree of that volume
    /// instead of walking the disk again. Returns null for imported folders and for paths that no
    /// scanned volume covers.
    /// </summary>
    private ScanReport? ReportFor(string path) =>
        string.IsNullOrEmpty(path)
            ? null
            : _reports.FirstOrDefault(report => path.StartsWith(report.Volume.RootPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stops whatever is running: a scan, an import, the web page, or gathering the gallery.</summary>
    private void CancelRunningWork()
    {
        _scanCancellation?.Cancel();
        _galleryCancellation?.Cancel();
    }

    private void ShowFolder(string path)
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

        ScanReport? report = ReportFor(ActiveFolderPath);
        if (report is not null && report.Aggregation.TryGetRecordNumber(ActiveFolderPath, out uint resolvedRecord))
        {
            _appliedSuffixes = DecodeSetup.Extensions;
            foreach (FileEntry entry in report.Index.GetChildren(
                report.Aggregation,
                resolvedRecord,
                ActiveFolderPath,
                DecodeSetup.Extensions))
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
            IReadOnlyList<string> suffixes = DecodeSetup.Extensions;
            IReadOnlyList<FileEntry> entries = await Task.Run(
                () => FileSystemListing.EnumerateChildren(path, suffixes),
                cancellationToken);
            _appliedSuffixes = suffixes;

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
        ShowFolder(parent);
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
            PreviewMessage = IsDecodableSuffix(item.Name)
                ? $"No preview for this file type. Size: {item.SizeText}."
                : $"No preview for this file type. Size: {item.SizeText}. Add its suffix under Decode settings to read it as a data file.";
            return;
        }

        if (item.PreviewKind == PreviewKind.Binary)
        {
            _ = LoadDecodedAsync(item, cancellation.Token);
            return;
        }

        _ = LoadPreviewAsync(item, cancellation.Token);
    }

    /// <summary>
    /// Reads the selected data file with the schematic the preview pane is set to, and shows the
    /// picture it came up with.
    /// </summary>
    private async Task LoadDecodedAsync(FileEntry item, CancellationToken cancellationToken)
    {
        IReadOnlyList<RawSchema> schemas = SchemasForPreview();
        if (schemas.Count == 0)
        {
            SetPreviewState(null, null, ShowAllSchematics
                ? "No data schematic is ticked for showing several at once. Tick some in Decode settings."
                : "There is no data schematic to read this file with. Open Decode settings to add one.");
            return;
        }

        try
        {
            IReadOnlyList<DecodeTile> tiles = await _previewService.DecodeAsync(
                item.FullPath,
                schemas,
                StretchPreview,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SetPreviewState(tiles);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SetPreviewState(null, null, exception.Message);
        }
    }

    /// <summary>The schematics to read the selected file with, in the order they are drawn.</summary>
    private IReadOnlyList<RawSchema> SchemasForPreview()
    {
        if (ShowAllSchematics)
        {
            return DecodeSetup.ShownSchemas;
        }

        if (SelectedPreviewSchema is { } chosen)
        {
            return new[] { chosen };
        }

        return PreviewSchemas.Count > 0 ? new[] { PreviewSchemas[0] } : Array.Empty<RawSchema>();
    }

    private bool IsDecodableSuffix(string name) => RawFileTypes.Matches(DecodeSetup.Extensions, name);

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

        PreviewTiles.Clear();
        IsTilePreviewVisible = false;
        IsImagePreviewVisible = image is not null;
        IsTextPreviewVisible = !string.IsNullOrEmpty(text);
        IsMessageVisible = !IsImagePreviewVisible && !IsTextPreviewVisible;
    }

    /// <summary>Shows what the decoder drew, one tile per schematic, and nothing else.</summary>
    private void SetPreviewState(IReadOnlyList<DecodeTile> tiles)
    {
        PreviewImage = null;
        PreviewText = string.Empty;
        PreviewMessage = string.Empty;

        PreviewTiles.Clear();
        foreach (DecodeTile tile in tiles)
        {
            PreviewTiles.Add(tile);
        }

        IsImagePreviewVisible = false;
        IsTextPreviewVisible = false;
        IsTilePreviewVisible = PreviewTiles.Count > 0;
        IsMessageVisible = !IsTilePreviewVisible;
    }

    /// <summary>
    /// Called when the decode settings change: the schematics on offer are read again, and the
    /// files of the folder on screen are looked at again with the new list of suffixes.
    /// </summary>
    private void OnDecodeSettingsChanged()
    {
        RebuildPreviewSchemas();
        ResetStretchToDefault();

        if (ActiveFolderPath.Length == 0 || !SuffixesChanged())
        {
            StartPreviewLoad();
            return;
        }

        _appliedSuffixes = DecodeSetup.Extensions;
        string? keep = SelectedContent?.FullPath;
        var items = Contents.ToList();

        Contents.Clear();
        foreach (FileEntry entry in items)
        {
            if (!entry.IsDirectory)
            {
                entry.PreviewKind = PreviewClassifier.Classify(entry.Name, DecodeSetup.Extensions);
            }

            Contents.Add(entry);
        }

        if (keep is not null)
        {
            SelectedContent = Contents.FirstOrDefault(entry => string.Equals(entry.FullPath, keep, StringComparison.OrdinalIgnoreCase));
        }

        // The list was rebuilt, so the preview is asked again whether or not the selection changed.
        StartPreviewLoad();
    }

    /// <summary>Refreshes the drop down of schematics, keeping the chosen one when it is still there.</summary>
    private void RebuildPreviewSchemas()
    {
        RawSchema? previous = _selectedPreviewSchema;
        IReadOnlyList<RawSchema> schemas = DecodeSetup.UsableSchemas;

        PreviewSchemas = schemas;

        // The choice is written straight to the field, so that the setter - which reloads the
        // preview - does not fire in the middle of a settings change. The caller asks for one
        // reload at the end instead.
        RawSchema? next = previous is not null && schemas.Contains(previous)
            ? previous
            : schemas.FirstOrDefault();

        if (!ReferenceEquals(next, _selectedPreviewSchema))
        {
            _selectedPreviewSchema = next;
            OnPropertyChanged(nameof(SelectedPreviewSchema));
        }

        OnPropertyChanged(nameof(AllSchematicsSummary));
    }

    /// <summary>True when the list of decoded file suffixes is not the one the folder was listed with.</summary>
    private bool SuffixesChanged() =>
        !_appliedSuffixes.SequenceEqual(DecodeSetup.Extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Puts the stretch tick back to what the schematics on show call for: on when any of them is a
    /// 16 bit layout, off when they are all 8 bit. It is called whenever the file or the schematics
    /// in view change, so the tick always starts from the sensible answer and the user is only
    /// ever overriding it for the look they are in the middle of.
    /// </summary>
    private void ResetStretchToDefault()
    {
        IReadOnlyList<RawSchema> schemas = SchemasForPreview();

        CanStretchPreview = schemas.Any(schema => RawDataTypes.CanStretch(schema.DataType));

        bool stretch = schemas.Any(schema => RawDataTypes.StretchesByDefault(schema.DataType));
        if (_stretchPreview != stretch)
        {
            _stretchPreview = stretch;
            OnPropertyChanged(nameof(StretchPreview));
        }
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
