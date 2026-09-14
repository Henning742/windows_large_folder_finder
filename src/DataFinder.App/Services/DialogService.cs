using System.Windows;
using Microsoft.Win32;

namespace DataFinder.App.Services;

public sealed class DialogService : IDialogService
{
    private const string TextFileFilter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";

    public void ShowError(string message, string title = "Error") =>
        Show(message, title, MessageBoxImage.Error);

    public void ShowInfo(string message, string title = "Information") =>
        Show(message, title, MessageBoxImage.Information);

    public string? OpenTextFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = TextFileFilter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveTextFile(string title, string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = TextFileFilter,
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = suggestedFileName,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void Show(string message, string title, MessageBoxImage icon) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, icon);
}

