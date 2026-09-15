using DataFinder.Core.Models;
using DataFinder.Core.Results;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class ResultTreeTests
{
    [Fact]
    public void BuildsThePathWithTheSeparatorItWasGiven()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[] { Folder("/home/lin/data/set1") });

        Assert.Equal("/", roots[0].FullPath);
        Assert.Equal("/home", roots[0].Children[0].FullPath);

        ResultTreeNode match = Assert.Single(ResultTree.All(roots), node => node.IsMatch);
        Assert.Equal("/home/lin/data/set1", match.FullPath);
    }

    [Fact]
    public void AddsUpWhatIsBelowAFolderThatDidNotMatch()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[]
        {
            Folder(@"D:\data\set1", size: 3L * 1024 * 1024 * 1024),
            Folder(@"D:\data\set2", size: 1L * 1024 * 1024 * 1024),
        });

        ResultTreeNode drive = Assert.Single(roots);
        ResultTreeNode data = Assert.Single(drive.Children);

        Assert.Equal(4L * 1024 * 1024 * 1024, data.ShownSizeBytes);
        Assert.Equal("4 GB", data.SizeText);
        Assert.Contains("2 folders below it", data.DetailText, StringComparison.Ordinal);
        Assert.Equal(4L * 1024 * 1024 * 1024, drive.ShownSizeBytes);
        Assert.Equal(2, drive.MatchesBelow);
    }

    [Fact]
    public void DoesNotAddAFolderThatMatchedToTheFoldersBelowIt()
    {
        // D:\data is a match of its own and holds a match, so it is already part of its own size.
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[]
        {
            Folder(@"D:\data", size: 10L * 1024 * 1024 * 1024),
            Folder(@"D:\data\set1", size: 2L * 1024 * 1024 * 1024),
        });

        ResultTreeNode drive = Assert.Single(roots);
        ResultTreeNode data = Assert.Single(drive.Children);

        Assert.Equal(10L * 1024 * 1024 * 1024, data.ShownSizeBytes);
        Assert.Equal("10 GB", data.SizeText);
        Assert.Equal(2L * 1024 * 1024 * 1024, Assert.Single(data.Children).ShownSizeBytes);

        // The drive is not a match, so it shows the one match under it and not both.
        Assert.Equal(10L * 1024 * 1024 * 1024, drive.ShownSizeBytes);
        Assert.Equal(2, drive.MatchesBelow);
    }

    [Fact]
    public void LeavesAFolderThatDidNotMatchWithoutASizeUntilSomethingMatches()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[] { Folder(@"D:\data\set1", size: 1024) });

        ResultTreeNode drive = Assert.Single(roots);
        ResultTreeNode data = Assert.Single(drive.Children);

        Assert.Equal("1 KB", drive.SizeText);
        Assert.Equal("1 KB", data.SizeText);
        Assert.Empty(drive.FilesText);
        Assert.Empty(drive.SubfolderText);
    }

    [Fact]
    public void PutsAFolderUnderTheFoldersThatLeadToIt()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[] { Folder(@"D:\data\set1") });

        ResultTreeNode drive = Assert.Single(roots);
        Assert.Equal(@"D:\", drive.Name);
        Assert.Equal(@"D:\", drive.FullPath);
        Assert.False(drive.IsMatch);

        ResultTreeNode data = Assert.Single(drive.Children);
        Assert.Equal("data", data.Name);
        Assert.Equal(@"D:\data", data.FullPath);
        Assert.False(data.IsMatch);
        Assert.Equal(1, data.Depth);

        ResultTreeNode set = Assert.Single(data.Children);
        Assert.Equal("set1", set.Name);
        Assert.Equal(@"D:\data\set1", set.FullPath);
        Assert.True(set.IsMatch);
        Assert.Equal(2, set.Depth);
    }

    [Fact]
    public void SharesTheFoldersOnTheWayToTwoMatches()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[]
        {
            Folder(@"D:\data\set1"),
            Folder(@"D:\data\set2\raw"),
        });

        ResultTreeNode data = Assert.Single(Assert.Single(roots).Children);
        Assert.Equal(new[] { "set1", "set2" }, data.Children.Select(node => node.Name).ToArray());

        ResultTreeNode set2 = data.Children[1];
        Assert.False(set2.IsMatch);
        Assert.Equal("raw", Assert.Single(set2.Children).Name);
    }

    [Fact]
    public void OrdersSiblingsByNameWhateverOrderTheResultsArrive()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[]
        {
            Folder(@"D:\b\zebra"),
            Folder(@"D:\a\zebra"),
            Folder(@"D:\a\apple"),
            Folder(@"C:\a"),
        });

        Assert.Equal(new[] { @"C:\", @"D:\" }, roots.Select(node => node.Name).ToArray());
        Assert.Equal(new[] { "a", "b" }, roots[1].Children.Select(node => node.Name).ToArray());
        Assert.Equal(new[] { "apple", "zebra" }, roots[1].Children[0].Children.Select(node => node.Name).ToArray());
    }

    [Fact]
    public void KeepsOneNodeForAFolderThatMatchesTwice()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[]
        {
            Folder(@"D:\data\set1", size: 10),
            Folder(@"D:\data\set1", size: 20),
        });

        ResultTreeNode data = Assert.Single(Assert.Single(roots).Children);
        ResultTreeNode set = Assert.Single(data.Children);
        Assert.Equal(@"D:\data\set1", set.FullPath);
        Assert.NotNull(set.Result);
    }

    [Theory]
    [InlineData(@"D:\data\set1", new[] { @"D:\", "data", "set1" })]
    [InlineData(@"D:\data\", new[] { @"D:\", "data" })]
    [InlineData(@"D:", new[] { @"D:\" })]
    [InlineData(@"\\server\share\data", new[] { @"\\server\share\", "data" })]
    [InlineData(@"/tmp/a", new[] { "/", "tmp", "a" })]
    public void SplitsAPathIntoItsParts(string path, string[] expected)
    {
        Assert.Equal(expected, ResultTree.SplitPath(path).ToArray());
    }

    [Fact]
    public void HidesTheFoldersBelowACollapsedOne()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[]
        {
            Folder(@"D:\data\set1"),
            Folder(@"D:\data\set2"),
        });

        ResultTreeNode data = Assert.Single(Assert.Single(roots).Children);
        Assert.Equal(
            new[] { @"D:\", "data", "set1", "set2" },
            ResultTree.Visible(roots).Select(node => node.Name).ToArray());

        data.IsExpanded = false;

        Assert.Equal(
            new[] { @"D:\", "data" },
            ResultTree.Visible(roots).Select(node => node.Name).ToArray());

        Assert.Equal(
            new[] { @"D:\", "data", "set1", "set2" },
            ResultTree.All(roots).Select(node => node.Name).ToArray());
    }

    [Fact]
    public void ExpandsNewFoldersSoTheMatchesAreVisible()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[] { Folder(@"D:\data\set1") });

        Assert.True(Assert.Single(roots).IsExpanded);
        Assert.True(Assert.Single(Assert.Single(roots).Children).IsExpanded);
    }

    [Fact]
    public void ReportsNoTreeForAnEmptyResult()
    {
        Assert.Empty(ResultTree.Build(Array.Empty<FolderResult>()));
        Assert.Empty(ResultTree.Build(new[] { Folder("   ") }));
    }

    [Fact]
    public void TellsTheTreeWhenAFolderIsExpandedOrCollapsed()
    {
        ResultTreeNode node = Assert.Single(ResultTree.Build(new[] { Folder(@"D:\data") }));
        var changed = new List<string?>();
        node.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        node.IsExpanded = false;
        node.IsExpanded = false;

        Assert.Equal(new[] { nameof(ResultTreeNode.IsExpanded) }, changed);
    }

    [Fact]
    public void ShowsTheNoteThatIsKeptOnTheFolder()
    {
        FolderResult folder = Folder(@"D:\data\set1");
        folder.Comment = "checked earlier";

        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[] { folder });
        ResultTreeNode match = Assert.Single(Assert.Single(Assert.Single(roots).Children).Children);

        Assert.Equal("checked earlier", match.Comment);
    }

    [Fact]
    public void WritesANewNoteBackToTheFolderAndTellsTheList()
    {
        FolderResult folder = Folder(@"D:\data\set1");
        ResultTreeNode match = Assert.Single(Assert.Single(Assert.Single(ResultTree.Build(new[] { folder })).Children).Children);
        var changed = new List<string?>();
        match.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        match.Comment = "worth another look";
        match.Comment = "worth another look";

        Assert.Equal("worth another look", folder.Comment);
        Assert.Equal(new[] { nameof(ResultTreeNode.Comment) }, changed);
    }

    [Fact]
    public void HasNoNoteOnTheFoldersThatDidNotMatch()
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(new[] { Folder(@"D:\data\set1") });
        ResultTreeNode drive = Assert.Single(roots);

        Assert.Empty(drive.Comment);

        drive.Comment = "this folder did not match the rules";

        Assert.Empty(drive.Comment);
        Assert.Null(drive.Result);
    }

    private static FolderResult Folder(string path, long size = 1024) => new()
    {
        FullPath = path,
        SizeBytes = size,
        TotalSizeBytes = size,
    };
}
