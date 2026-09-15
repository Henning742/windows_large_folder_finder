using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DataFinder.App.Services;
using DataFinder.App.ViewModels;

namespace DataFinder.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainViewModel(new DialogService());
        DataContext = ViewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public MainViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.InitializeAsync();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Notes that were never exported are worth a question before they are gone for good.
        if (!ViewModel.CanClose())
        {
            e.Cancel = true;
            return;
        }

        ViewModel.OnClosing();
    }

    /// <summary>
    /// Opens the setup dialog. It closes with "true" when the user asked for the scan, which is the
    /// only moment a run starts.
    /// </summary>
    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScanDialog(ViewModel.ScanSetup) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            await ViewModel.StartScanAsync();
        }
    }

    private void ContentsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedContent is null)
        {
            return;
        }

        ViewModel.ActivateSelectedContent();
        e.Handled = true;
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedResultNode is null)
        {
            return;
        }

        ViewModel.OpenSelectedResultFolder();
        e.Handled = true;
    }

    /// <summary>
    /// Opens the decode settings. They are edited where they live, so the preview follows along
    /// while the dialog is open.
    /// </summary>
    private void DecodeSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DecodeDialog(ViewModel.DecodeSetup) { Owner = this };
        dialog.ShowDialog();
    }
}
