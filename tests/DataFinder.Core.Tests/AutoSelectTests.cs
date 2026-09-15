using DataFinder.Core.Models;
using DataFinder.Core.Preview;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class AutoSelectTests
{
    [Fact]
    public void PicksTheFirstFileAndSkipsTheFoldersAtTheTop()
    {
        FileEntry? picked = AutoSelect.Pick(Folder("sub1", "sub2", "a.txt", "b.txt"), AutoSelectMode.FirstFile);

        Assert.NotNull(picked);
        Assert.Equal("a.txt", picked.Name);
    }

    [Fact]
    public void PicksTheFileInTheMiddleOfTheFolder()
    {
        FileEntry? picked = AutoSelect.Pick(Folder("a.txt", "b.txt", "c.txt", "d.txt", "e.txt"), AutoSelectMode.MiddleFile);

        Assert.Equal("c.txt", picked!.Name);
    }

    [Fact]
    public void PicksARandomFileFromTheWholeFolder()
    {
        FileEntry? picked = AutoSelect.Pick(Folder("a.txt", "b.txt", "c.txt"), AutoSelectMode.RandomFile, new Random(1));

        Assert.Contains(picked!.Name, new[] { "a.txt", "b.txt", "c.txt" });
    }

    [Fact]
    public void DoesNotAlwaysPickTheSameFileInRandomMode()
    {
        var picked = new HashSet<string>();

        for (int seed = 0; seed < 20; seed++)
        {
            picked.Add(AutoSelect.Pick(Folder("a.txt", "b.txt", "c.txt"), AutoSelectMode.RandomFile, new Random(seed))!.Name);
        }

        Assert.True(picked.Count > 1);
    }

    [Fact]
    public void SelectsNothingWhenTheUserAskedForIt()
    {
        Assert.Null(AutoSelect.Pick(Folder("a.txt"), AutoSelectMode.None));
    }

    [Fact]
    public void SelectsNothingWhenTheFolderHasNoFiles()
    {
        Assert.Null(AutoSelect.Pick(Folder("sub1", "sub2"), AutoSelectMode.FirstFile));
        Assert.Null(AutoSelect.Pick(Array.Empty<FileEntry>(), AutoSelectMode.MiddleFile));
    }

    private static IReadOnlyList<FileEntry> Folder(params string[] names) =>
        names.Select(name => new FileEntry
        {
            Name = name,
            FullPath = @"D:\data\" + name,
            IsDirectory = !name.Contains('.', StringComparison.Ordinal),
        }).ToList();
}
