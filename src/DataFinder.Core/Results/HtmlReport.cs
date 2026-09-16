using System.Globalization;
using System.Text;
using DataFinder.Core.Util;

namespace DataFinder.Core.Results;

/// <summary>
/// One picture to show in the report. The bytes are written next to the page as a file of their own
/// rather than carried inside it, so a report over hundreds of folders stays small enough to open.
/// The picture carries its own caption so the page does not have to know how it was made.
/// </summary>
public sealed record HtmlReportPicture(string Caption, string Source, string? Note = null);

/// <summary>
/// One picture file the page needs beside it: where it sits relative to the page, and what to write
/// there. The path is always written with forward slashes, which is what the page links to.
/// </summary>
public sealed record HtmlReportFile(string RelativePath, byte[] Bytes);

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

    /// <summary>A few pictures of what is inside the folder, as files pointing at the page's folder.</summary>
    public IReadOnlyList<HtmlReportPicture> Images { get; init; } = Array.Empty<HtmlReportPicture>();

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
/// Writes the whole result list out as one HTML page: a tree of every folder on the left, and the
/// folders that matched on the right with a few thumbnails of what is inside them.
///
/// The pictures are written beside the page as files of their own rather than carried inside it,
/// and the page asks the browser for one only when it comes near the screen. That is what keeps a
/// report over a thousand folders opening as quickly as a report over three, and it is why there is
/// no size a report has to stay under: the page is a list of names until somebody looks at it.
/// </summary>
public static class HtmlReport
{
    public static string Build(
        DateTimeOffset generatedAt,
        IReadOnlyList<HtmlReportFolder> folders,
        HtmlReportOptions? options = null,
        string? pictureFolder = null)
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

        WriteHeader(page, folders, options, generatedAt, topLevelSize, matches, pictureFolder);

        page.AppendLine("<div class=\"page\">");
        WriteContents(page, folders);
        WriteSplitter(page);
        WriteFolders(page, folders);
        page.AppendLine("</div>");

        page.AppendLine(Script);
        page.AppendLine("</body>");
        page.AppendLine("</html>");
        return page.ToString();
    }

    /// <summary>
    /// Writes the report and its pictures to a file, as UTF-8 with a byte order mark so any browser
    /// reads it right. The pictures go into the folder the page points at, which is created if it is
    /// not there yet.
    /// </summary>
    public static void Save(
        string path,
        DateTimeOffset generatedAt,
        IReadOnlyList<HtmlReportFolder> folders,
        HtmlReportOptions? options = null,
        IReadOnlyList<HtmlReportFile>? pictures = null,
        string? pictureFolder = null)
    {
        // The pictures go first: a page that points at files which were never written is worse than
        // no page at all.
        WritePictures(path, pictures);
        File.WriteAllText(path, Build(generatedAt, folders, options, pictureFolder), new UTF8Encoding(true));
    }

    /// <summary>
    /// Writes a page that has already been built. Reports are written as UTF-8 with a byte order
    /// mark, so a browser reads a folder name in any language the way it was meant.
    /// </summary>
    public static void SaveText(string path, string html) =>
        File.WriteAllText(path, html, new UTF8Encoding(true));

    /// <summary>Writes the picture files the page points at, in the folder under the page.</summary>
    public static void WritePictures(string pagePath, IReadOnlyList<HtmlReportFile>? pictures)
    {
        if (pictures is null || pictures.Count == 0)
        {
            return;
        }

        string? folder = Path.GetDirectoryName(Path.GetFullPath(pagePath));
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        foreach (HtmlReportFile picture in pictures)
        {
            string target = Path.Combine(folder, picture.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string? directory = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(target, picture.Bytes);
        }
    }

    private static void WriteHeader(
        StringBuilder page,
        IReadOnlyList<HtmlReportFolder> folders,
        HtmlReportOptions options,
        DateTimeOffset generatedAt,
        long topLevelSize,
        int matches,
        string? pictureFolder)
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

        // Where the pictures are, said once at the top: the page is a folder of files that travel
        // together with it, and the arrangement is worth knowing before the page is sent on.
        if (!string.IsNullOrWhiteSpace(pictureFolder) && folders.Any(folder => folder.Images.Count > 0))
        {
            page.Append("<p class=\"where\">The pictures sit in the <strong>")
                .Append(Escape(pictureFolder!))
                .AppendLine("</strong> folder beside this page; copy the two together, and they load as you scroll.</p>");

            // The pictures are asked for as they come near the screen, which takes a script. Without
            // one the folders, sizes and notes are all still there; only the pictures are not.
            page.Append("<noscript><p class=\"warning\">The pictures are asked for as you come to them, " +
                        "which needs JavaScript turned on. The folders, their sizes and their notes are all " +
                        "below either way, and the pictures themselves are in the <strong>")
                .Append(Escape(pictureFolder!))
                .AppendLine("</strong> folder beside this page.</p></noscript>");
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

    /// <summary>
    /// The bar between the tree and the folders. It is dragged left and right, so either side can be
    /// given the room: the tree for a long path, the folders for a row of thumbnails.
    /// </summary>
    private static void WriteSplitter(StringBuilder page) =>
        page.AppendLine(
            "<div class=\"splitter\" id=\"splitter\" role=\"separator\" aria-orientation=\"vertical\" " +
            "tabindex=\"0\" title=\"Drag left or right to widen or narrow the tree; double click to put it back\"></div>");

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

            // Every row gives its first column to the twisty, whether it folds or not, so names and
            // sizes line up down the whole list instead of stepping sideways at every branch.
            page.AppendLine("<li>");

            if (branch)
            {
                // A branch folds away on its own, so a list of hundreds of folders can be walked a
                // level at a time.
                page.AppendLine("<details open>");
                page.AppendLine("<summary><span class=\"twisty\" aria-hidden=\"true\"></span>");
                WriteRow(page, item);
                page.AppendLine("</summary>");
                WriteList(page, item.Children, depth + 1);
                page.AppendLine("</details>");
            }
            else
            {
                page.AppendLine("<div class=\"leaf\"><span class=\"twisty\" aria-hidden=\"true\"></span>");
                WriteRow(page, item);
                page.AppendLine("</div>");
            }

            page.AppendLine("</li>");
        }

        page.AppendLine("</ul>");
    }

    /// <summary>
    /// The part of a row that is clicked: the folder's own name, its size, and - for a folder that
    /// is only on the way to matches - how many matches sit below it.
    /// </summary>
    private static void WriteRow(StringBuilder page, TreeItem item)
    {
        HtmlReportFolder folder = item.Folder;

        page.Append("<a class=\"row");
        if (!folder.IsMatch)
        {
            page.Append(" parent");
        }

        page.Append("\" href=\"#f").Append(item.Section).Append('"');
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

        page.AppendLine("</a>");
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
            page.AppendLine("<summary><span class=\"twisty\" aria-hidden=\"true\"></span>");
            page.Append("<span class=\"name\" title=\"").Append(Escape(folder.FullPath)).Append("\">")
                .Append(Escape(folder.FullPath)).Append("</span>");
            page.Append("<span class=\"size\">");
            page.Append(folder.Exists ? ByteSize.Format(folder.SizeBytes) : "not found");
            page.AppendLine("</span>");
            page.AppendLine("</summary>");

            page.Append("<p class=\"counts\" title=\"").Append(Escape(Counts(folder))).Append("\">")
                .Append(Counts(folder)).AppendLine("</p>");

            // The note and the line about the pictures are written whether they hold anything or
            // not: a row that is there in every folder is what makes every folder the same height.
            page.Append("<p class=\"comment\"");

            if (!string.IsNullOrWhiteSpace(folder.Comment))
            {
                page.Append(" title=\"").Append(Escape(folder.Comment)).Append('"');
            }

            page.Append('>');

            if (!string.IsNullOrWhiteSpace(folder.Comment))
            {
                page.Append("<span class=\"label\">Note:</span> ").Append(Escape(folder.Comment));
            }

            page.AppendLine("</p>");

            WritePictures(page, folder);
            WritePicturesNote(page, folder);

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
        // One box for the pictures whether there are any or not, so a folder with nothing to show
        // is the same height as one with a row of thumbnails in it.
        page.AppendLine("<div class=\"shots\">");

        if (!folder.Exists)
        {
            page.AppendLine("<p class=\"empty\">The folder is not there any more, so there is nothing to show.</p>");
            page.AppendLine("</div>");
            return;
        }

        if (folder.Images.Count == 0)
        {
            page.Append("<p class=\"empty\">")
                .Append(Escape(folder.PicturesNote ?? "Nothing in this folder could be shown as a picture."))
                .AppendLine("</p>");
            page.AppendLine("</div>");
            return;
        }

        page.AppendLine("<div class=\"thumbs\">");

        foreach (HtmlReportPicture image in folder.Images)
        {
            page.AppendLine("<figure>");
            // The size of the box is written into the page, so the folder is the height it is going
            // to be before the picture arrives - and the picture is left in the data-src until the
            // script at the foot of the page sees it come near the screen.
            page.Append("<img alt=\"").Append(Escape(image.Caption))
                .Append("\" width=\"220\" height=\"140\" decoding=\"async\" data-src=\"")
                .Append(Escape(image.Source)).AppendLine("\">");
            page.Append("<figcaption><span class=\"file\" title=\"").Append(Escape(image.Caption)).Append("\">")
                .Append(Escape(image.Caption)).Append("</span>");

            if (!string.IsNullOrWhiteSpace(image.Note))
            {
                page.Append("<span class=\"detail\" title=\"").Append(Escape(image.Note!)).Append("\">")
                    .Append(Escape(image.Note!)).Append("</span>");
            }
            else
            {
                page.Append("<span class=\"detail\"></span>");
            }

            page.AppendLine("</figcaption>");
            page.AppendLine("</figure>");
        }

        page.AppendLine("</div>");
        page.AppendLine("</div>");
    }

    /// <summary>
    /// The line under the pictures: how many of how many were shown, and whether the report was
    /// full. It is written whether it holds anything or not, so that every folder is the same
    /// height - a folder that says something here is exactly as tall as one that does not.
    /// </summary>
    private static void WritePicturesNote(StringBuilder page, HtmlReportFolder folder)
    {
        if (folder.Images.Count == 0 || string.IsNullOrWhiteSpace(folder.PicturesNote))
        {
            page.AppendLine("<p class=\"hint\"></p>");
            return;
        }

        page.Append("<p class=\"hint\" title=\"").Append(Escape(folder.PicturesNote!)).Append("\">")
            .Append(Escape(folder.PicturesNote!)).AppendLine("</p>");
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
          /* The tree, the bar that is dragged to resize it, and the folders. The width of the tree
             is a variable so the drag, the remembered width and the stylesheet all say one thing. */
          .page { display: grid; grid-template-columns: var(--nav-width, minmax(220px, 24%)) 8px minmax(0, 1fr);
                  gap: 6px; padding: 16px 22px 40px; }
          .splitter { align-self: stretch; cursor: col-resize; background: #e3e6ea; border-radius: 3px;
                      touch-action: none; }
          .splitter:hover, .splitter:focus-visible, body.resizing .splitter { background: #0a66c2; outline: none; }
          body.resizing { cursor: col-resize; user-select: none; }
          nav { position: sticky; top: 16px; align-self: start; max-height: calc(100vh - 32px); overflow: auto;
                background: #ffffff; border: 1px solid #dcdfe4; border-radius: 6px; padding: 10px 12px; }
          nav h2 { margin: 0 0 8px; font-size: 15px; }

          /* The contents tree. Each row is the twisty in a column of its own and then the row
             itself, which in turn holds the name, the size and the "N below" note. Names, sizes and
             notes therefore line up all the way down. One level of nesting steps in by a few pixels
             only - enough to read by, so that a deep path does not eat the width of the pane. */
          ul.tree { list-style: none; margin: 0; padding: 0; }
          ul.tree ul.tree { margin-left: 2px; padding-left: 4px; border-left: 1px solid #e3e6ea; }
          ul.tree li { margin: 1px 0; }
          ul.tree summary, ul.tree .leaf { display: grid; grid-template-columns: 16px minmax(0, 1fr);
                                           align-items: center; }
          ul.tree summary { list-style: none; cursor: pointer; }
          ul.tree summary::-webkit-details-marker { display: none; }
          /* The last column keeps its width whether a row has a note there or not, so the sizes line
             up down the list instead of stepping sideways wherever a folder has no matches below. */
          ul.tree .row { display: grid; grid-template-columns: minmax(0, 1fr) auto 76px; column-gap: 8px;
                         align-items: baseline; padding: 3px 6px; border-radius: 3px; color: #1b1b1b;
                         text-decoration: none; }
          ul.tree .row:hover { background: #eef4fd; }
          ul.tree .row.current { background: #e6f0ff; box-shadow: inset 2px 0 0 #0a66c2; }
          ul.tree .row .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
          ul.tree .row.parent .name { color: #4b5563; }
          ul.tree .row .size { color: #0a66c2; font-variant-numeric: tabular-nums; }
          ul.tree .row .count { color: #6b7280; font-size: 11px; white-space: nowrap; text-align: right; }

          /* The twisty fills the first column of a row whether the row folds or not, so a folder
             without children keeps its name in the same place as one with children. */
          .twisty { width: 16px; height: 16px; display: inline-flex; align-items: center;
                    justify-content: center; color: #6b7280; font-size: 9px; line-height: 1;
                    user-select: none; }
          details > summary .twisty::before { content: "\25B6"; }
          details[open] > summary .twisty::before { content: "\25BC"; }

          main { display: flex; flex-direction: column; gap: 12px; min-width: 0; }

          /* The folders. Every one of them is the same height - one line per note, and a row of
             pictures that is one row whatever it holds - so the page is the height it is going to
             be from the start, and the scroll bar does not move as the pictures arrive. */
          section.folder { background: #ffffff; border: 1px solid #dcdfe4; border-radius: 6px; }
          section.folder details > summary { cursor: pointer; padding: 10px 14px; display: grid;
                                             grid-template-columns: 16px minmax(0, 1fr) auto; column-gap: 10px;
                                             align-items: baseline; list-style: none; }
          section.folder details > summary::-webkit-details-marker { display: none; }
          section.folder summary .name { font-weight: 600; white-space: nowrap; overflow: hidden;
                                         text-overflow: ellipsis; }
          section.folder summary .size { color: #0a66c2; font-variant-numeric: tabular-nums; white-space: nowrap; }

          /* One line each, whatever they hold: the whole of it is in the tooltip, and the folder is
             the height it was. The least height is set rather than left to the text, so that a row
             with nothing in it is the same height as a row with something - which is what makes a
             folder with no note exactly as tall as one with a note. */
          section.folder .counts, section.folder .comment, section.folder .hint {
                                 color: #4b5563; white-space: nowrap; overflow: hidden;
                                 text-overflow: ellipsis; }
          section.folder .counts { margin: 0 14px 10px; min-height: 1.45em; }
          section.folder .comment { margin: 0 14px 10px; min-height: calc(1.45em + 12px);
                                    background: #fff8e1; border: 1px solid #f0e0b0; border-radius: 4px;
                                    padding: 5px 8px; }
          section.folder .hint { margin: 0 14px; min-height: 1.45em; }
          section.folder .comment:empty, section.folder .hint:empty { visibility: hidden; }
          section.folder .label { font-weight: 600; color: #6b7280; }
          section.folder :target { outline: none; }

          /* The room under the last row is padding rather than a margin: a margin there is the one
             thing that could step outside the folder and leave it a different height from the rest. */
          section.folder details { padding-bottom: 10px; }

          /* The row of pictures: one line, as wide as the folder, scrolling sideways when there are
             more pictures than the width takes. One row is one height, whether it holds one picture
             or twenty. */
          .shots { margin: 0 14px 10px; height: 212px; }
          .thumbs { display: flex; flex-wrap: nowrap; gap: 10px; height: 100%; overflow-x: auto;
                    overflow-y: hidden; align-items: flex-start; padding-bottom: 4px; }
          .shots .empty { margin: 0; color: #4b5563; }
          figure { margin: 0; flex: 0 0 auto; width: 220px; background: #fafbfc; border: 1px solid #e3e6ea;
                   border-radius: 5px; padding: 5px; }
          /* A picture that has not been asked for yet is a dark box of the size it will be, with no
             line of text in it: the folder is the height it is going to have all along. */
          figure img { display: block; width: 100%; height: 140px; object-fit: contain; background: #111;
                       border-radius: 3px; color: transparent; }
          figcaption { display: flex; flex-direction: column; gap: 2px; padding-top: 4px; }
          figcaption .file { font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
          figcaption .detail { font-size: 11px; color: #6b7280; white-space: nowrap; overflow: hidden;
                               text-overflow: ellipsis; }
          section.folder:target { border-color: #0a66c2; box-shadow: 0 0 0 2px rgba(10,102,194,.15); }
          @media (max-width: 900px) { .page { grid-template-columns: 1fr; } .splitter { display: none; }
                                      nav { position: static; max-height: none; } }
        </style>
        """;

    private const string Script = """
        <script>
          (function () {
            var sections = Array.prototype.slice.call(
              document.querySelectorAll('section.folder details, nav details'));
            var rows = Array.prototype.slice.call(document.querySelectorAll('ul.tree a.row'));

            // The pictures. A report over hundreds of folders has thousands of them, and asking a
            // browser for all of them at once costs the reading of every one of those files before
            // the first folder can be looked at. Each one is asked for as it comes near the screen
            // instead - the row being read and the row under it, not the row a thousand folders
            // down. A browser that cannot watch for that, or a page where this went wrong, is given
            // every picture at once: the watching is an economy, not a requirement.
            (function () {
              var pictures = Array.prototype.slice.call(document.querySelectorAll('img[data-src]'));
              if (pictures.length === 0) { return; }

              function ask(picture) {
                if (!picture.getAttribute('src')) {
                  picture.setAttribute('src', picture.getAttribute('data-src'));
                }
              }

              function askForEveryOne() { pictures.forEach(ask); }

              try {
                if (!('IntersectionObserver' in window)) { askForEveryOne(); return; }

                var watcher = new IntersectionObserver(function (entries) {
                  entries.forEach(function (entry) {
                    if (!entry.isIntersecting) { return; }
                    ask(entry.target);
                    watcher.unobserve(entry.target);
                  });
                }, { rootMargin: '800px 600px' });

                pictures.forEach(function (picture) { watcher.observe(picture); });
              } catch (error) {
                askForEveryOne();
              }
            })();

            document.getElementById('expandAll').addEventListener('click', function () {
              sections.forEach(function (details) { details.open = true; });
            });

            document.getElementById('collapseAll').addEventListener('click', function () {
              sections.forEach(function (details) { details.open = false; });
            });

            // Marks the row of the folder being looked at, so a long list still says where the reader is.
            function markCurrent() {
              var id = location.hash.slice(1);
              rows.forEach(function (row) {
                row.classList.toggle('current', id.length > 0 && row.getAttribute('href') === '#' + id);
              });
            }

            function openTarget() {
              var id = location.hash.slice(1);
              if (!id) { return; }
              var section = document.getElementById(id);
              if (!section) { return; }

              // Every branch on the way to the row has to be open before it can be scrolled to.
              rows.forEach(function (row) {
                if (row.getAttribute('href') !== '#' + id) { return; }
                var details = row.parentElement;
                while (details) {
                  if (details.tagName === 'DETAILS') { details.open = true; }
                  details = details.parentElement;
                }
              });

              section.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }

            window.addEventListener('hashchange', function () { openTarget(); markCurrent(); });
            openTarget();
            markCurrent();

            // The bar between the tree and the folders is dragged left or right, so either side can
            // be given the room. Where it was left is remembered for the next time the page is
            // opened, and a double click puts it back where the stylesheet wants it.
            (function () {
              var page = document.querySelector('.page');
              var tree = document.getElementById('contents');
              var splitter = document.getElementById('splitter');
              if (!page || !tree || !splitter) { return; }

              var least = 150;   // a folder name and its size still have to fit in the tree
              var most = 900;    // and the folders keep the rest of the window
              var stored = null;

              try { stored = window.localStorage.getItem('datafinder:treeWidth'); } catch (error) { stored = null; }

              var width = parseFloat(stored);
              if (isFinite(width) && width > 0) { setWidth(width); }

              function clamp(value) {
                var room = page.clientWidth - 220;
                return Math.max(least, Math.min(value, Math.max(least, Math.min(most, room))));
              }

              function setWidth(value) {
                page.style.setProperty('--nav-width', Math.round(clamp(value)) + 'px');
              }

              function currentWidth() { return tree.getBoundingClientRect().width; }

              function remember() {
                try { window.localStorage.setItem('datafinder:treeWidth', String(Math.round(currentWidth()))); }
                catch (error) { }
              }

              function forget() {
                page.style.removeProperty('--nav-width');
                try { window.localStorage.removeItem('datafinder:treeWidth'); } catch (error) { }
              }

              splitter.addEventListener('pointerdown', function (event) {
                if (event.pointerType === 'mouse' && event.button !== 0) { return; }

                var startX = event.clientX;
                var startWidth = currentWidth();
                event.preventDefault();
                document.body.classList.add('resizing');

                // Keeping the pointer on the bar means the drag carries on when it leaves the bar,
                // which is what a divider is expected to do.
                try { splitter.setPointerCapture(event.pointerId); } catch (error) { }

                function move(moved) { setWidth(startWidth + (moved.clientX - startX)); }

                function stop() {
                  document.body.classList.remove('resizing');
                  splitter.removeEventListener('pointermove', move);
                  splitter.removeEventListener('pointerup', stop);
                  splitter.removeEventListener('pointercancel', stop);
                  remember();
                }

                splitter.addEventListener('pointermove', move);
                splitter.addEventListener('pointerup', stop);
                splitter.addEventListener('pointercancel', stop);
              });

              // A double click puts the tree back to the width the stylesheet asks for.
              splitter.addEventListener('dblclick', forget);

              splitter.addEventListener('keydown', function (event) {
                var step = event.shiftKey ? 60 : 20;
                if (event.key === 'ArrowLeft') { setWidth(currentWidth() - step); remember(); }
                else if (event.key === 'ArrowRight') { setWidth(currentWidth() + step); remember(); }
                else if (event.key === 'Home' || event.key === 'End') { forget(); }
                else { return; }
                event.preventDefault();
              });
            })();
          })();
        </script>
        """;
}
