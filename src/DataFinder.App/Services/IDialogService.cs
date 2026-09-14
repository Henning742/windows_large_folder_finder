namespace DataFinder.App.Services;

/// <summary>Keeps the view models free of direct MessageBox / file dialog calls.</summary>
public interface IDialogService
{
    void ShowError(string message, string title = "Error");

    void ShowInfo(string message, string title = "Information");

    string? OpenTextFile(string title);

    string? SaveTextFile(string title, string suggestedFileName);
}

