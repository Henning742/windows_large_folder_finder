using System.Windows;
using DataFinder.App.ViewModels;

namespace DataFinder.App;

/// <summary>Asks which drives to read and which rules to apply before a scan is started.</summary>
public partial class ScanDialog : Window
{
    public ScanDialog(ScanDialogViewModel viewModel)
    {
        InitializeComponent();

        ViewModel = viewModel;
        DataContext = viewModel;

        viewModel.ScanRequested += OnScanRequested;
        Closed += OnClosed;
    }

    public ScanDialogViewModel ViewModel { get; }

    /// <summary>Closing with "true" means the user asked for the scan to start.</summary>
    private void OnScanRequested() => DialogResult = true;

    private void OnClosed(object? sender, EventArgs e) => ViewModel.ScanRequested -= OnScanRequested;
}
