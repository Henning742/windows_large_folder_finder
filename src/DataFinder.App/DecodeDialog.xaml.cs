using System.Windows;
using DataFinder.App.ViewModels;

namespace DataFinder.App;

/// <summary>
/// Asks which file types the preview should read as data, and which schematics it should try. The
/// settings belong to the main window's view model, so anything changed here is in force straight
/// away - there is nothing to apply and nothing to cancel.
/// </summary>
public partial class DecodeDialog : Window
{
    public DecodeDialog(DecodeDialogViewModel viewModel)
    {
        InitializeComponent();

        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public DecodeDialogViewModel ViewModel { get; }
}
