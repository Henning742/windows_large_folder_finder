using System.Collections.ObjectModel;
using System.Windows.Media;
using DataFinder.App.Infrastructure;
using DataFinder.App.Services;

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
/// picture itself drawn down to the size a card shows. It is drawn once, when the folder is read,
/// and kept with the picture, so scrolling back to a card does not read the file a second time.
/// </summary>
public sealed record GalleryPicture(string Caption, string Detail, string FullPath, ImageSource? Thumbnail)
{
    /// <summary>What the card says about a picture while the mouse rests on it.</summary>
    public string Hint => $"Double click to open {FullPath}";
}

/// <summary>
/// One folder in the gallery: where it is, what is in it, and the pictures that were picked out of
/// it. The pictures are not found when the card is made, but when the card comes into view - and let
/// go of again when it goes away - so that a gallery of a thousand folders holds the pictures of the
/// dozen being looked at rather than the pictures of all of them.
/// </summary>
public sealed class GalleryFolder : ObservableObject
{
    /// <summary>What a card says before its folder has been looked into.</summary>
    public const string Waiting = "The pictures are found when this card comes into view.";

    private readonly ObservableCollection<GalleryPicture> _pictures = new();

    private string _status = Waiting;
    private string? _note;
    private bool _loaded;
    private bool _loading;

    /// <summary>
    /// Which asking for the pictures is the current one. A card that goes off the screen while its
    /// pictures are being found leaves the answer to that asking behind, and this is what says so.
    /// </summary>
    private int _generation;

    /// <summary>The last part of the path, which is what the card is headed with.</summary>
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    /// <summary>The size and the file counts of the folder, in one line.</summary>
    public string Summary { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;

    /// <summary>False for a folder that is not there any more, which is said rather than looked for.</summary>
    public bool Exists { get; init; } = true;

    /// <summary>How the pictures of a folder are found. The pane puts it there when it shows the gallery.</summary>
    public GalleryLoader? Loader { get; set; }

    public ObservableCollection<GalleryPicture> Pictures => _pictures;

    public bool HasComment => Comment.Length > 0;

    /// <summary>True while there is a row of pictures to draw on the card.</summary>
    public bool HasPictures => _pictures.Count > 0;

    /// <summary>What there is to say about the pictures - that none could be made, or that some were left out.</summary>
    public string? Note
    {
        get => _note;
        private set
        {
            if (SetProperty(ref _note, value))
            {
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    public bool HasNote => !string.IsNullOrEmpty(_note);

    /// <summary>What the card says where the pictures go: looking inside, or why there are none.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => _status.Length > 0;

    /// <summary>
    /// Finds the pictures of this folder, once, if they are not in hand. The card calls this as it
    /// comes into view; what comes back is the row of pictures for the card to draw, or the line that
    /// says why there are none.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded || _loading || Loader is not { } loader)
        {
            return;
        }

        if (!Exists)
        {
            Note = "The folder is not there any more, so there is nothing to show.";
            Status = Note;
            _loaded = true;
            return;
        }

        int generation = ++_generation;
        _loading = true;
        Status = "Looking inside...";

        try
        {
            GalleryFolderPictures found = await loader.LoadAsync(FullPath);

            // The card went off the screen while this was being found, so what came back is not
            // wanted - it is let go of with the release that let this asking go.
            if (generation != _generation)
            {
                return;
            }

            foreach (GalleryPicture picture in found.Pictures)
            {
                _pictures.Add(picture);
            }

            Note = found.Note;
            Status = found.Pictures.Count > 0
                ? string.Empty
                : found.Note ?? "Nothing in this folder could be shown as a picture.";
            _loaded = true;
            OnPropertyChanged(nameof(HasPictures));
        }
        catch (OperationCanceledException)
        {
            // The run this card was asking of was stopped. The card goes back to waiting, so that
            // coming to it again asks the run that replaced the stopped one.
            if (generation == _generation)
            {
                Status = Waiting;
            }
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                Status = exception.Message;
                _loaded = true;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _loading = false;
            }
        }
    }

    /// <summary>
    /// Lets go of the pictures of a card that has gone off the screen, so that scrolling a long list
    /// does not gather up the pictures of every folder passed on the way.
    /// </summary>
    public void Release()
    {
        // Whatever was in flight is no longer wanted: the card it was for has gone.
        _generation++;

        if (!_loaded && !_loading)
        {
            return;
        }

        _loading = false;
        _loaded = false;
        _pictures.Clear();
        Note = null;
        Status = Waiting;
        OnPropertyChanged(nameof(HasPictures));
    }
}
