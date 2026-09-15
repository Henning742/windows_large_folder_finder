using System.Windows;
using Microsoft.Win32;

namespace DataFinder.App.Services;

public sealed class DialogService : IDialogService
{
    private const string ReadFilter =
        "Results (*.csv;*.txt)|*.csv;*.txt|CSV reports (*.csv)|*.csv|Older text reports (*.txt)|*.txt|All files (*.*)|*.*";

    private const string WriteFilter = "CSV report (*.csv)|*.csv|All files (*.*)|*.*";

    public void ShowError(string message, string title = "Error") =>
        Show(message, title, MessageBoxImage.Error);

    public void ShowInfo(string message, string title = "Information") =>
        Show(message, title, MessageBoxImage.Information);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) ==
        MessageBoxResult.Yes;

    public string? OpenReportFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = ReadFilter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveReportFile(string title, string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = WriteFilter,
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = suggestedFileName,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void Show(string message, string title, MessageBoxImage icon) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, icon);
}
