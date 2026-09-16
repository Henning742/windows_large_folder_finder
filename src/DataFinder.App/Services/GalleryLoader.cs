using System.IO;
using DataFinder.App.ViewModels;
using DataFinder.Core.Models;
using DataFinder.Core.Preview.Raw;
using DataFinder.Core.Results;

namespace DataFinder.App.Services;

/// <summary>What came out of looking inside one folder: the pictures to draw, and the line about them.</summary>
public sealed record GalleryFolderPictures(IReadOnlyList<GalleryPicture> Pictures, string? Note);

/// <summary>
/// Finds what is worth looking at inside a folder and draws it down to the size a card shows.
///
/// The gallery keeps one of these while a list is on show and asks it about a folder when that
/// folder's card comes into view. That is the whole of the economy: the reading of the disk and the
/// memory for the pictures are for the cards being looked at and no others, so the gallery can hold
/// a list of any length without a bound on how much picture it may carry.
/// </summary>
public sealed class GalleryLoader
{
    /// <summary>How wide a picture is drawn before the card that shows it takes it on.</summary>
    private const int ThumbnailPixelWidth = 320;

    private readonly Func<string, IReadOnlyList<FileEntry>> _listFiles;
    private readonly IReadOnlyList<string> _suffixes;
    private readonly IReadOnlyList<RawSchema> _schemas;
    private readonly bool _stretch;
    private readonly FolderPictureFinder _finder;
    private readonly CancellationTokenSource _cancellation = new();

    public GalleryLoader(
        Func<string, IReadOnlyList<FileEntry>> listFiles,
        IReadOnlyList<string> suffixes,
        IReadOnlyList<RawSchema> schemas,
        bool stretch,
        int picturesPerFolder,
        Random? random = null)
    {
        _listFiles = listFiles;
        _suffixes = suffixes;
        _schemas = schemas;
        _stretch = stretch;

        // No bound on the size of a picture or on the lot of them: what is carried is only ever the
        // cards on screen, and a card carries the pictures of its own folder.
        _finder = new FolderPictureFinder(
            new HtmlReportLimits { PicturesPerFolder = picturesPerFolder },
            random);
    }

    /// <summary>
    /// Looks inside one folder and draws what is there. It runs off the thread the window is drawn
    /// on - the pictures come back frozen, so they can be handed straight to the cards.
    /// </summary>
    public async Task<GalleryFolderPictures> LoadAsync(string folderPath)
    {
        CancellationToken token = _cancellation.Token;

        return await Task.Run(
            () =>
            {
                FolderPictures found = _finder.Find(
                    folderPath, _listFiles, _suffixes, _schemas, _stretch, token);

                var pictures = new List<GalleryPicture>(found.Pictures.Count);
                foreach (FolderPreviewPicture picture in found.Pictures)
                {
                    pictures.Add(new GalleryPicture(
                        picture.Caption,
                        picture.Note,
                        Path.Combine(folderPath, picture.Caption),
                        PreviewService.ToThumbnail(picture.Bytes, ThumbnailPixelWidth)));
                }

                return new GalleryFolderPictures(pictures, found.Note);
            },
            token);
    }

    /// <summary>Stops the lookings that are under way, which is what leaving the gallery amounts to.</summary>
    public void Cancel() => _cancellation.Cancel();
}
