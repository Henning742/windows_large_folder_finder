namespace DataFinder.App.ViewModels;

/// <summary>Which of the two things the right pane shows.</summary>
public enum RightPaneMode
{
    /// <summary>The contents of the folder picked on the left, and a preview of the file picked there.</summary>
    Contents,

    /// <summary>
    /// Every folder of the list at once, each with a few pictures of what is inside it - the way the
    /// web page shows them.
    /// </summary>
    AllFolders,
}

/// <summary>One entry of the right pane's drop down.</summary>
public sealed record RightPaneOption(RightPaneMode Mode, string Text);

/// <summary>
/// One picture on a folder's card in the gallery: the file it came from, how it was read, and the
/// bytes themselves. The bytes are turned into something to draw by the card that shows them, and
/// only then - so a gallery of hundreds of folders decodes the few pictures being looked at.
/// </summary>
public sealed record GalleryPicture(string Caption, string Detail, string FullPath, byte[] Bytes)
{
    /// <summary>What the card says about a picture while the mouse rests on it.</summary>
    public string Hint => $"Double click to open {FullPath}";
}

/// <summary>
/// One folder in the gallery: where it is, what is in it, the note that was typed about it, and the
/// few pictures that were picked out of it at random.
/// </summary>
public sealed class GalleryFolder
{
    /// <summary>The last part of the path, which is what the card is headed with.</summary>
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    /// <summary>The size and the file counts of the folder, in one line.</summary>
    public string Summary { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;

    /// <summary>What there is to say about the pictures - that none could be made, or that some were left out.</summary>
    public string? Note { get; init; }

    public IReadOnlyList<GalleryPicture> Pictures { get; init; } = Array.Empty<GalleryPicture>();

    public bool HasComment => Comment.Length > 0;

    public bool HasNote => !string.IsNullOrEmpty(Note);
}
