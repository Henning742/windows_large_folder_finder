using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DataFinder.App.Services;
using DataFinder.App.ViewModels;
using DataFinder.Core.Results;

namespace DataFinder.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainViewModel(new DialogService());
        DataContext = ViewModel;
        ViewModel.ResultRowRevealed += OnResultRowRevealed;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public MainViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.InitializeAsync();

    /// <summary>
    /// Brings a row of the result list into view. The view model works out which row, and the list
    /// is the only thing that knows how to scroll to it - which is what "Show in the tree" needs
    /// after the gallery has picked a folder somewhere down a long list.
    /// </summary>
    private void OnResultRowRevealed(object? sender, ResultTreeNode node)
    {
        try
        {
            ResultsList.ScrollIntoView(node);
            ResultsList.UpdateLayout();
        }
        catch (InvalidOperationException)
        {
            // The row is not part of the list on screen - a filter can hide it - and there is then
            // nothing to scroll to. The folder is still picked, so the pane on the right follows.
        }
    }

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

    /// <summary>
    /// Double clicking a picture in the gallery opens the file it was made from, the same way double
    /// clicking a row of the contents list does.
    /// </summary>
    private void GalleryPicture_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not GalleryPicture picture)
        {
            return;
        }

        ShellService.OpenFile(picture.FullPath);
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
