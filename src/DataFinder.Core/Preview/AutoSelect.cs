using DataFinder.Core.Models;

namespace DataFinder.Core.Preview;

/// <summary>Which item the app selects on its own when a folder is opened.</summary>
public enum AutoSelectMode
{
    /// <summary>The first file in the folder.</summary>
    FirstFile,

    /// <summary>The file in the middle of the folder.</summary>
    MiddleFile,

    /// <summary>A file picked at random.</summary>
    RandomFile,

    /// <summary>Nothing is selected, so the preview stays empty until a row is clicked.</summary>
    None,
}

/// <summary>Chooses the row to select when a folder is opened, so the preview is never empty.</summary>
public static class AutoSelect
{
    /// <summary>
    /// The item to select, or <c>null</c> when the user asked for nothing or the folder holds no
    /// files at all. Folders are skipped on purpose: a fresh folder usually starts with subfolders,
    /// and a preview of a file is more useful than a row that only says "Folder".
    /// </summary>
    public static FileEntry? Pick(IReadOnlyList<FileEntry> items, AutoSelectMode mode, Random? random = null)
    {
        if (items is null || mode == AutoSelectMode.None)
        {
            return null;
        }

        var files = items.Where(item => !item.IsDirectory).ToList();
        if (files.Count == 0)
        {
            return null;
        }

        return mode switch
        {
            AutoSelectMode.MiddleFile => files[files.Count / 2],
            AutoSelectMode.RandomFile => files[(random ?? Random.Shared).Next(files.Count)],
            _ => files[0],
        };
    }
}
