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

        Assert.Contains("<li><a href=\"#f1\" class=\"parent\" title=\"D:\\\">", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#f1\" class=\"parent\" title=\"D:\\one\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#f2\" class=\"parent\" title=\"D:\\two\"", html, StringComparison.Ordinal);
        Assert.Contains("<section class=\"folder\" id=\"f2\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PutsThePicturesInAsPartsOfThePageItself()
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
                    images: new[] { new HtmlReportImage("shot.png", "image/png", new byte[] { 1, 2, 3 }, "120 KB, a picture") }),
            });

        Assert.Contains("src=\"data:image/png;base64,AQID\"", html, StringComparison.Ordinal);
        Assert.Contains("<figcaption><span class=\"file\">shot.png</span>", html, StringComparison.Ordinal);
        Assert.Contains("120 KB, a picture", html, StringComparison.Ordinal);
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
                Match(@"D:\data\set1", "set1", 2, 1024, images: new[] { new HtmlReportImage("a.png", "image/png", new byte[] { 9 }) }),
                Match(@"D:\data\set2", "set2", 2, 1024),
                Parent(@"D:\other", "other", 1, 0, matchesBelow: 0),
            });

        foreach (string tag in new[] { "ul", "li", "nav", "main", "section", "details", "figure", "div", "html", "body" })
        {
            int open = Count(html, $"<{tag}");
            int close = Count(html, $"</{tag}>");
            Assert.Equal(open, close);
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
        IReadOnlyList<HtmlReportImage>? images = null,
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
        Images = images ?? Array.Empty<HtmlReportImage>(),
        PicturesNote = picturesNote,
    };
}
