namespace DataFinder.App.Services;

/// <summary>Keeps the view models free of direct MessageBox / file dialog calls.</summary>
public interface IDialogService
{
    void ShowError(string message, string title = "Error");

    void ShowInfo(string message, string title = "Information");

    /// <summary>Asks a yes/no question. "No" is the answer when the user just closes the box.</summary>
    bool Confirm(string message, string title);

    /// <summary>Asks for a report to import. CSV and the older text reports are both offered.</summary>
    string? OpenReportFile(string title);

    /// <summary>Asks where the CSV report and its JSON companion file should be written.</summary>
    string? SaveReportFile(string title, string suggestedFileName);

    /// <summary>Asks where the web page report should be written.</summary>
    string? SaveWebPageFile(string title, string suggestedFileName);
}
