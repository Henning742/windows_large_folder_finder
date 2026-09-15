using DataFinder.Core.Results;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class HtmlReportTests
{
    private static readonly DateTimeOffset Written = new(2026, 9, 15, 10, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void WritesTheTreeOnTheLeftAndASectionPerFolderThatMatched()
    {
        string html = HtmlReport.Build(
            Written,
            new[]
            {
                Parent("D:\\", "D:\\", 0, 3L * 1024 * 1024 * 1024, matchesBelow: 1),
                Parent(@"D:\data", "data", 1, 3L * 1024 * 1024 * 1024, matchesBelow: 1),
                Match(@"D:\data\set1", "set1", 2, 3L * 1024 * 1024 * 1024, comment: "worth another look"),
            },
            new HtmlReportOptions { Title = "Folders found", Source = "D: - size > 200 MB" });

        Assert.Contains("<nav id=\"contents\">", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"name\">set1</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"size\">3 GB</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"count\">1 below</span>", html, StringComparison.Ordinal);
        Assert.Contains("<section class=\"folder\" id=\"f1\">", html, StringComparison.Ordinal);
        Assert.Contains("D:\\data\\set1", html, StringComparison.Ordinal);
        Assert.Contains("worth another look", html, StringComparison.Ordinal);
        Assert.Contains("D: - size &gt; 200 MB", html, StringComparison.Ordinal);
        Assert.Contains("1 folder matched", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SendsAClickInTheTreeToTheFirstFolderBelowIt()
    {
        // Two matches under two different parents: each parent has to lead somewhere of its own.
        string html = HtmlReport.Build(
            Written,
            new[]
            {
                Parent("D:\\", "D:\\", 0, 2L * 1024 * 1024 * 1024, matchesBelow: 2),
                Parent(@"D:\one", "one", 1, 1L * 1024 * 1024 * 1024, matchesBelow: 1),
                Match(@"D:\one\a", "a", 2, 1L * 1024 * 1024 * 1024),
                Parent(@"D:\two", "two", 1, 1L * 1024 * 1024 * 1024, matchesBelow: 1),
                Match(@"D:\two\b", "b", 2, 1L * 1024 * 1024 * 1024),
            });

        Assert.Contains("<a class=\"row parent\" href=\"#f1\" title=\"D:\\\">", html, StringComparison.Ordinal);
        Assert.Contains("class=\"row parent\" href=\"#f1\" title=\"D:\\one\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"row parent\" href=\"#f2\" title=\"D:\\two\"", html, StringComparison.Ordinal);
        Assert.Contains("<section class=\"folder\" id=\"f2\">", html, StringComparison.Ordinal);

        // A folder with folders under it folds away in the contents, like the sections do, and the
        // twisty takes a column of its own so the name starts where the names above it start.
        Assert.Contains("<details open>", html, StringComparison.Ordinal);
        Assert.Contains("<summary><span class=\"twisty\" aria-hidden=\"true\"></span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PointsThePicturesAtFilesBesideThePage()
    {
        string html = HtmlReport.Build(
            Written,
            new[]
            {
                Match(
                    @"D:\data\set1",
                    "set1",
                    0,
                    1024,
                    images: new[] { new HtmlReportPicture("shot.png", "report.files/0001-shot.png", "120 KB, a picture") }),
            });

        Assert.Contains("src=\"report.files/0001-shot.png\"", html, StringComparison.Ordinal);
        Assert.Contains("<figcaption><span class=\"file\">shot.png</span>", html, StringComparison.Ordinal);
        Assert.Contains("120 KB, a picture", html, StringComparison.Ordinal);

        // Nothing is carried inside the page: a report over hundreds of folders would be too big.
        Assert.DoesNotContain("base64", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"data:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysWhenAFolderHasNothingToShow()
    {
        string html = HtmlReport.Build(
            Written,
            new[] { Match(@"D:\data\set1", "set1", 0, 1024, picturesNote: "Nothing among the 412 files here could be shown as a picture.") });

        Assert.Contains("Nothing among the 412 files here could be shown", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysWhenAFolderIsGone()
    {
        string html = HtmlReport.Build(
            Written,
            new[] { Match(@"D:\data\set1", "set1", 0, 0, exists: false) });

        Assert.Contains("not found", html, StringComparison.Ordinal);
        Assert.Contains("The folder is not there any more", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapesWhatWouldOtherwiseBreakThePage()
    {
        string html = HtmlReport.Build(
            Written,
            new[]
            {
                Match(@"D:\a<b&c", "a<b&\"c'", 0, 1024, comment: "look <script>alert(1)</script> here"),
            });

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("look &lt;script&gt;alert(1)&lt;/script&gt; here", html, StringComparison.Ordinal);
        Assert.Contains("a&lt;b&amp;&quot;c&#39;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HasNothingToShowWhenThereAreNoFolders()
    {
        string html = HtmlReport.Build(Written, Array.Empty<HtmlReportFolder>());

        Assert.Contains("Nothing was found", html, StringComparison.Ordinal);
        Assert.Contains("No folder matched the rules.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesThePageWithBalancedTags()
    {
        string html = HtmlReport.Build(
            Written,
            new[]
            {
                Parent("D:\\", "D:\\", 0, 3L * 1024 * 1024 * 1024, matchesBelow: 2),
                Parent(@"D:\data", "data", 1, 2L * 1024 * 1024 * 1024, matchesBelow: 1),
                Match(@"D:\data\set1", "set1", 2, 1024, images: new[] { new HtmlReportPicture("a.png", "report.files/0001-a.png") }),
                Match(@"D:\data\set2", "set2", 2, 1024),
                Parent(@"D:\other", "other", 1, 0, matchesBelow: 0),
            });

        foreach (string tag in new[] { "ul", "li", "nav", "main", "section", "details", "summary", "figure", "div", "html", "body" })
        {
            int open = Count(html, $"<{tag}");
            int close = Count(html, $"</{tag}>");
            Assert.Equal(open, close);
        }
    }

    [Fact]
    public void StepsEachLevelOfTheTreeInByALittle()
    {
        string html = HtmlReport.Build(
            Written,
            new[]
            {
                Parent("D:\\", "D:\\", 0, 3L * 1024 * 1024 * 1024, matchesBelow: 1),
                Parent(@"D:\data", "data", 1, 3L * 1024 * 1024 * 1024, matchesBelow: 1),
                Match(@"D:\data\set1", "set1", 2, 1024),
            });

        // One nested list per level, and the stylesheet steps each of them in by a few pixels only,
        // so that a deep path does not eat the width of the pane.
        Assert.Equal(3, Count(html, "<ul class=\"tree\">"));
        Assert.Contains("ul.tree ul.tree { margin-left: 2px; padding-left: 4px; border-left: 1px solid #e3e6ea; }", html, StringComparison.Ordinal);

        // A folder without children still gets the twisty column, so its name lines up with the rest.
        Assert.Contains("<div class=\"leaf\"><span class=\"twisty\" aria-hidden=\"true\"></span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PutsABarBetweenTheTreeAndTheFoldersThatCanBeDragged()
    {
        string html = HtmlReport.Build(
            Written,
            new[]
            {
                Parent("D:\\", "D:\\", 0, 3L * 1024 * 1024 * 1024, matchesBelow: 1),
                Match(@"D:\data\set1", "set1", 1, 1024),
            });

        // The bar sits between the tree and the folders, and says what it is for.
        int tree = html.IndexOf("<nav id=\"contents\">", StringComparison.Ordinal);
        int bar = html.IndexOf("id=\"splitter\"", StringComparison.Ordinal);
        int folders = html.IndexOf("<main>", StringComparison.Ordinal);

        Assert.True(tree >= 0 && bar > tree && folders > bar, "the bar should sit between the tree and the folders");
        Assert.Contains("role=\"separator\"", html, StringComparison.Ordinal);

        // Dragging it writes the width of the tree, which the stylesheet reads: one thing says what
        // the width is, so the drag, the remembered width and the layout cannot disagree.
        Assert.Contains("cursor: col-resize", html, StringComparison.Ordinal);
        Assert.Contains("var(--nav-width, minmax(220px, 24%)) 8px minmax(0, 1fr)", html, StringComparison.Ordinal);
        Assert.Contains("setProperty('--nav-width'", html, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem('datafinder:treeWidth'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesThePicturesAsFilesBesideThePage()
    {
        string folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "report.html");

        try
        {
            HtmlReport.Save(
                path,
                Written,
                new[]
                {
                    Match(
                        @"D:\data\set1",
                        "set1",
                        0,
                        1024,
                        images: new[] { new HtmlReportPicture("shot.png", "report.files/0001-shot.png") }),
                },
                options: null,
                pictures: new[] { new HtmlReportFile("report.files/0001-shot.png", new byte[] { 1, 2, 3 }) });

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(folder, "report.files", "0001-shot.png")));
            Assert.Contains("src=\"report.files/0001-shot.png\"", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void WritesTheFileAsUtf8WithAByteOrderMark()
    {
        string folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "report.html");

        try
        {
            HtmlReport.Save(path, Written, new[] { Match(@"D:\data\set1", "set1", 0, 1024) });

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Contains("set1", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        int index = text.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }

        return count;
    }

    private static HtmlReportFolder Parent(string path, string name, int depth, long size, int matchesBelow) => new()
    {
        FullPath = path,
        Name = name,
        Depth = depth,
        IsMatch = false,
        SizeBytes = size,
        MatchesBelow = matchesBelow,
    };

    private static HtmlReportFolder Match(
        string path,
        string name,
        int depth,
        long size,
        string comment = "",
        bool exists = true,
        IReadOnlyList<HtmlReportPicture>? images = null,
        string? picturesNote = null) => new()
    {
        FullPath = path,
        Name = name,
        Depth = depth,
        IsMatch = true,
        SizeBytes = size,
        TotalSizeBytes = size,
        DirectFileCount = 412,
        TotalFileCount = 1590,
        SubfolderCount = 3,
        Comment = comment,
        Exists = exists,
        Images = images ?? Array.Empty<HtmlReportPicture>(),
        PicturesNote = picturesNote,
    };
}
