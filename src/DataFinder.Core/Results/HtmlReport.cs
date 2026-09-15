using System.Globalization;
using System.Text;
using DataFinder.Core.Util;

namespace DataFinder.Core.Results;

/// <summary>
/// One picture to put in the report, already in the bytes a browser can show. The picture carries
/// its own caption so the report does not have to know how it was made.
/// </summary>
public sealed record HtmlReportImage(string Caption, string MimeType, byte[] Bytes, string? Note = null);

/// <summary>
/// One row of the report: a folder that matched the rules, or a folder on the way to one. The list
/// is flat and in path order, with <see cref="Depth"/> saying how deep a row sits, which is exactly
/// what the tree in the window works from.
/// </summary>
public sealed class HtmlReportFolder
{
    public required string FullPath { get; init; }

    /// <summary>The last part of the path, which is what the tree shows.</summary>
    public required string Name { get; init; }

    public int Depth { get; init; }

    /// <summary>False for a folder that is only on the way to the folders that matched.</summary>
    public bool IsMatch { get; init; }

    /// <summary>
    /// What the row shows: a folder that matched shows its own size, a folder that is only the way
    /// to matches shows the sum of the folders below it.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>The size of everything in the folder, for the folders that matched.</summary>
    public long TotalSizeBytes { get; init; }

    public long DirectFileCount { get; init; }

    public long TotalFileCount { get; init; }

    public int SubfolderCount { get; init; }

    /// <summary>How many folders below this one matched the rules.</summary>
    public int MatchesBelow { get; init; }

    public string Comment { get; init; } = string.Empty;

    public bool Exists { get; init; } = true;

    /// <summary>A few pictures of what is inside the folder.</summary>
    public IReadOnlyList<HtmlReportImage> Images { get; init; } = Array.Empty<HtmlReportImage>();

    /// <summary>What there is to say about the pictures - that none could be made, or that some were left out.</summary>
    public string? PicturesNote { get; init; }
}

/// <summary>What goes above the list: where the folders came from and what the rules were.</summary>
public sealed class HtmlReportOptions
{
    public string Title { get; init; } = "NTFS Folder Finder";

    /// <summary>The drives and the rules the folders were found with.</summary>
    public string? Source { get; init; }

    /// <summary>Anything the scan had to report, such as a master file table that could not be read in full.</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Writes the whole result list out as one stand-alone HTML page: a tree of every folder on the
/// left, and the folders that matched on the right with a few thumbnails of what is inside them.
/// The pictures travel inside the file, so it can be sent on or opened years later and still show
/// them.
/// </summary>
public static class HtmlReport
{
    public static string Build(
        DateTimeOffset generatedAt,
        IReadOnlyList<HtmlReportFolder> folders,
        HtmlReportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        options ??= new HtmlReportOptions();

        var page = new StringBuilder();
        long topLevelSize = folders.Where(folder => folder.Depth == 0).Sum(folder => folder.SizeBytes);
        int matches = folders.Count(folder => folder.IsMatch);

        page.AppendLine("<!DOCTYPE html>");
        page.AppendLine("<html lang=\"en\">");
        page.AppendLine("<head>");
        page.AppendLine("<meta charset=\"utf-8\">");
        page.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        page.Append("<title>").Append(Escape(options.Title)).AppendLine("</title>");
        page.AppendLine(Style);
        page.AppendLine("</head>");
        page.AppendLine("<body>");

        WriteHeader(page, folders, options, generatedAt, topLevelSize, matches);

        page.AppendLine("<div class=\"page\">");
        WriteContents(page, folders);
        WriteFolders(page, folders);
        page.AppendLine("</div>");

        page.AppendLine(Script);
        page.AppendLine("</body>");
        page.AppendLine("</html>");
        return page.ToString();
    }

    /// <summary>Writes the report to a file, as UTF-8 with a byte order mark so any browser reads it right.</summary>
    public static void Save(
        string path,
        DateTimeOffset generatedAt,
        IReadOnlyList<HtmlReportFolder> folders,
        HtmlReportOptions? options = null) =>
        File.WriteAllText(path, Build(generatedAt, folders, options), new UTF8Encoding(true));

    /// <summary>
    /// Writes a page that has already been built. Reports are written as UTF-8 with a byte order
    /// mark, so a browser reads a folder name in any language the way it was meant.
    /// </summary>
    public static void SaveText(string path, string html) =>
        File.WriteAllText(path, html, new UTF8Encoding(true));

    private static void WriteHeader(
        StringBuilder page,
        IReadOnlyList<HtmlReportFolder> folders,
        HtmlReportOptions options,
        DateTimeOffset generatedAt,
        long topLevelSize,
        int matches)
    {
        page.AppendLine("<header>");
        page.Append("<h1>").Append(Escape(options.Title)).AppendLine("</h1>");

        page.Append("<p class=\"summary\">");
        page.Append(matches == 1 ? "1 folder" : $"{matches:N0} folders");
        page.Append(" matched, holding ").Append(ByteSize.Format(topLevelSize));
        page.Append(" in all. Written ").Append(Escape(generatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append('.');
        page.AppendLine("</p>");

        if (!string.IsNullOrWhiteSpace(options.Source))
        {
            page.Append("<p class=\"source\">").Append(Escape(options.Source!)).AppendLine("</p>");
        }

        if (!string.IsNullOrWhiteSpace(options.Warning))
        {
            page.Append("<p class=\"warning\">").Append(Escape(options.Warning!)).AppendLine("</p>");
        }

        page.AppendLine(
            "<p class=\"buttons\"><button type=\"button\" id=\"expandAll\">Expand all</button>" +
            "<button type=\"button\" id=\"collapseAll\">Collapse all</button>" +
            $"<span class=\"hint\">{(folders.Count == 1 ? "1 row" : $"{folders.Count:N0} rows")} in the tree</span></p>");
        page.AppendLine("</header>");
    }

    /// <summary>
    /// The nested list of every row. A row that matched links to its own section; a row that is
    /// only the way to matches links to the first section below it, so clicking anywhere in the
    /// tree lands on a folder with something in it.
    /// </summary>
    private static void WriteContents(StringBuilder page, IReadOnlyList<HtmlReportFolder> folders)
    {
        page.AppendLine("<nav id=\"contents\">");
        page.AppendLine("<h2>Contents</h2>");

        if (folders.Count == 0)
        {
            page.AppendLine("<p class=\"empty\">Nothing was found, so there is nothing here.</p>");
            page.AppendLine("</nav>");
            return;
        }

        WriteList(page, Nest(folders, SectionNumbers(folders)), 0);
        page.AppendLine("</nav>");
    }

    /// <summary>One item of the contents tree, with the section its link points at.</summary>
    private sealed record TreeItem(HtmlReportFolder Folder, int Section, List<TreeItem> Children);

    /// <summary>
    /// Turns the flat rows into the nested list the contents are drawn from. The rows arrive in
    /// path order with their depth, so the row above a deeper one is its parent.
    /// </summary>
    private static List<TreeItem> Nest(IReadOnlyList<HtmlReportFolder> folders, int[] sectionOf)
    {
        var roots = new List<TreeItem>();
        var open = new List<TreeItem>();

        for (int index = 0; index < folders.Count; index++)
        {
            var item = new TreeItem(folders[index], sectionOf[index], new List<TreeItem>());
            int depth = Math.Max(0, folders[index].Depth);

            while (open.Count > depth)
            {
                open.RemoveAt(open.Count - 1);
            }

            if (open.Count == 0)
            {
                roots.Add(item);
            }
            else
            {
                open[^1].Children.Add(item);
            }

            open.Add(item);
        }

        return roots;
    }

    private static void WriteList(StringBuilder page, IReadOnlyList<TreeItem> items, int depth)
    {
        if (items.Count == 0)
        {
            return;
        }

        page.AppendLine("<ul class=\"tree\">");

        foreach (TreeItem item in items)
        {
            HtmlReportFolder folder = item.Folder;
            bool branch = item.Children.Count > 0;

            page.Append("<li>");

            // A branch of the tree folds away on its own, so a list of hundreds of folders can be
            // walked a level at a time.
            if (branch)
            {
                page.Append("<details open><summary>");
            }

            page.Append("<a href=\"#f").Append(item.Section).Append('"');
            if (!folder.IsMatch)
            {
                page.Append(" class=\"parent\"");
            }

            page.Append(" title=\"").Append(Escape(folder.FullPath)).Append("\">");
            page.Append("<span class=\"name\">").Append(Escape(folder.Name)).Append("</span>");
            page.Append("<span class=\"size\">")
                .Append(folder.Exists ? ByteSize.Format(folder.SizeBytes) : "not found")
                .Append("</span>");

            if (!folder.IsMatch)
            {
                page.Append("<span class=\"count\">")
                    .Append(folder.MatchesBelow == 1 ? "1 below" : $"{folder.MatchesBelow:N0} below")
                    .Append("</span>");
            }

            page.Append("</a>");

            if (branch)
            {
                page.Append("</summary>");
                WriteList(page, item.Children, depth + 1);
                page.AppendLine("</details></li>");
                continue;
            }

            page.AppendLine("</li>");
        }

        page.AppendLine("</ul>");
    }

    private static void WriteFolders(StringBuilder page, IReadOnlyList<HtmlReportFolder> folders)
    {
        page.AppendLine("<main>");

        int section = 0;
        foreach (HtmlReportFolder folder in folders)
        {
            if (!folder.IsMatch)
            {
                continue;
            }

            section++;
            page.Append("<section class=\"folder\" id=\"f").Append(section).AppendLine("\">");
            page.AppendLine("<details open>");
            page.AppendLine("<summary>");
            page.Append("<span class=\"name\">").Append(Escape(folder.FullPath)).Append("</span>");
            page.Append("<span class=\"size\">");
            page.Append(folder.Exists ? ByteSize.Format(folder.SizeBytes) : "not found");
            page.AppendLine("</span>");
            page.AppendLine("</summary>");

            page.Append("<p class=\"counts\">").Append(Counts(folder)).AppendLine("</p>");

            if (!string.IsNullOrWhiteSpace(folder.Comment))
            {
                page.Append("<p class=\"comment\"><span class=\"label\">Note:</span> ")
                    .Append(Escape(folder.Comment)).AppendLine("</p>");
            }

            WritePictures(page, folder);

            page.AppendLine("</details>");
            page.AppendLine("</section>");
        }

        if (section == 0)
        {
            page.AppendLine("<p class=\"empty\">No folder matched the rules.</p>");
        }

        page.AppendLine("</main>");
    }

    private static void WritePictures(StringBuilder page, HtmlReportFolder folder)
    {
        if (!folder.Exists)
        {
            page.AppendLine("<p class=\"empty\">The folder is not there any more, so there is nothing to show.</p>");
            return;
        }

        if (folder.Images.Count == 0)
        {
            page.Append("<p class=\"empty\">")
                .Append(Escape(folder.PicturesNote ?? "Nothing in this folder could be shown as a picture."))
                .AppendLine("</p>");
            return;
        }

        page.AppendLine("<div class=\"thumbs\">");

        foreach (HtmlReportImage image in folder.Images)
        {
            page.AppendLine("<figure>");
            page.Append("<img alt=\"").Append(Escape(image.Caption)).Append("\" src=\"data:")
                .Append(Escape(image.MimeType)).Append(";base64,")
                .Append(Convert.ToBase64String(image.Bytes))
                .AppendLine("\">");
            page.Append("<figcaption><span class=\"file\">").Append(Escape(image.Caption)).Append("</span>");

            if (!string.IsNullOrWhiteSpace(image.Note))
            {
                page.Append("<span class=\"detail\">").Append(Escape(image.Note!)).Append("</span>");
            }

            page.AppendLine("</figcaption>");
            page.AppendLine("</figure>");
        }

        page.AppendLine("</div>");

        if (!string.IsNullOrWhiteSpace(folder.PicturesNote))
        {
            page.Append("<p class=\"hint\">").Append(Escape(folder.PicturesNote!)).AppendLine("</p>");
        }
    }

    private static string Counts(HtmlReportFolder folder)
    {
        if (!folder.Exists)
        {
            return "The folder does not exist (or is not reachable) right now.";
        }

        return
            $"{ByteSize.Format(folder.TotalSizeBytes)} in total, {folder.TotalFileCount:N0} files below, " +
            $"{folder.DirectFileCount:N0} files directly inside, {folder.SubfolderCount:N0} subfolders.";
    }

    /// <summary>
    /// The section each row links to: its own when it matched, and otherwise the first match below
    /// it. Rows that lead to the same match share a section, which is what makes a click anywhere
    /// in the tree useful.
    /// </summary>
    private static int[] SectionNumbers(IReadOnlyList<HtmlReportFolder> folders)
    {
        var numbers = new int[folders.Count];
        int section = 0;

        // The sections are written in the order the folders are listed, so the numbers are handed out
        // in that order first.
        for (int index = 0; index < folders.Count; index++)
        {
            if (folders[index].IsMatch)
            {
                numbers[index] = ++section;
            }
        }

        // Then every folder that is only on the way to matches takes the number of the next match
        // after it, which is the first one below it.
        int next = Math.Max(1, section);

        for (int index = folders.Count - 1; index >= 0; index--)
        {
            if (folders[index].IsMatch)
            {
                next = numbers[index];
            }
            else
            {
                numbers[index] = next;
            }
        }

        return numbers;
    }

    /// <summary>Turns the handful of characters that mean something in HTML into entities.</summary>
    public static string Escape(string? text) =>
        (text ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private const string Style = """
        <style>
          :root { color-scheme: light; }
          * { box-sizing: border-box; }
          body { margin: 0; font-family: "Segoe UI", system-ui, sans-serif; font-size: 14px;
                 line-height: 1.45; color: #1b1b1b; background: #f6f7f9; }
          header { padding: 18px 22px; background: #ffffff; border-bottom: 1px solid #dcdfe4; }
          header h1 { margin: 0 0 6px; font-size: 20px; }
          header p { margin: 2px 0; color: #4b5563; }
          .warning { color: #b00020; font-weight: 600; }
          .buttons { margin-top: 10px; display: flex; gap: 8px; align-items: center; }
          button { font: inherit; padding: 4px 10px; border: 1px solid #c3c8d0; border-radius: 4px;
                   background: #ffffff; cursor: pointer; }
          button:hover { border-color: #0a66c2; color: #0a66c2; }
          .hint { color: #6b7280; font-size: 12px; }
          .page { display: grid; grid-template-columns: minmax(260px, 22%) 1fr; gap: 16px; padding: 16px 22px 40px; }
          nav { position: sticky; top: 16px; align-self: start; max-height: calc(100vh - 32px); overflow: auto;
                background: #ffffff; border: 1px solid #dcdfe4; border-radius: 6px; padding: 10px 12px; }
          nav h2 { margin: 0 0 8px; font-size: 15px; }
          ul.tree { list-style: none; margin: 0; padding-left: 14px; }
          nav ul.tree { padding-left: 0; }
          ul.tree li { margin: 1px 0; }
          ul.tree a { display: flex; gap: 8px; align-items: baseline; padding: 2px 4px; border-radius: 3px;
                      color: #1b1b1b; text-decoration: none; }
          ul.tree a:hover { background: #eef4fd; color: #0a66c2; }
          ul.tree a .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
          ul.tree a.parent .name { color: #4b5563; }
          ul.tree a .size { color: #0a66c2; font-variant-numeric: tabular-nums; }
          ul.tree a .count { color: #6b7280; font-size: 11px; }
          main { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
          section.folder { background: #ffffff; border: 1px solid #dcdfe4; border-radius: 6px; }
          section.folder details > summary { cursor: pointer; padding: 10px 14px; display: flex; gap: 12px;
                                             justify-content: space-between; align-items: baseline; }
          section.folder summary .name { font-weight: 600; word-break: break-all; }
          section.folder summary .size { color: #0a66c2; font-variant-numeric: tabular-nums; white-space: nowrap; }
          section.folder .counts, section.folder .comment, section.folder .empty,
          section.folder .hint { margin: 0 14px 10px; color: #4b5563; }
          section.folder .comment { background: #fff8e1; border: 1px solid #f0e0b0; border-radius: 4px; padding: 6px 8px; }
          section.folder .label { font-weight: 600; color: #6b7280; }
          section.folder :target { outline: none; }
          .thumbs { display: flex; flex-wrap: wrap; gap: 10px; margin: 0 14px 12px; }
          figure { margin: 0; width: 220px; background: #fafbfc; border: 1px solid #e3e6ea; border-radius: 5px;
                   padding: 5px; }
          figure img { display: block; width: 100%; height: 140px; object-fit: contain; background: #111;
                       border-radius: 3px; }
          figcaption { display: flex; flex-direction: column; gap: 2px; padding-top: 4px; }
          figcaption .file { font-size: 12px; word-break: break-all; }
          figcaption .detail { font-size: 11px; color: #6b7280; }
          section.folder:target { border-color: #0a66c2; box-shadow: 0 0 0 2px rgba(10,102,194,.15); }
          @media (max-width: 900px) { .page { grid-template-columns: 1fr; } nav { position: static; max-height: none; } }
        </style>
        """;

    private const string Script = """
        <script>
          (function () {
            var sections = Array.prototype.slice.call(
              document.querySelectorAll('section.folder details, nav details'));

            document.getElementById('expandAll').addEventListener('click', function () {
              sections.forEach(function (details) { details.open = true; });
            });

            document.getElementById('collapseAll').addEventListener('click', function () {
              sections.forEach(function (details) { details.open = false; });
            });

            function openTarget() {
              var id = location.hash.slice(1);
              if (!id) { return; }
              var section = document.getElementById(id);
              if (!section) { return; }
              var details = section.querySelector('details');
              if (details) { details.open = true; }
              section.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }

            window.addEventListener('hashchange', openTarget);
            openTarget();
          })();
        </script>
        """;
}
