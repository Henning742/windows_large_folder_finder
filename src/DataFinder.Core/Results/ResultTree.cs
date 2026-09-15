using System.ComponentModel;
using DataFinder.Core.Models;
using DataFinder.Core.Util;

namespace DataFinder.Core.Results;

/// <summary>
/// A place in the result list. Every folder that matched the rules gets a node, and so does every
/// folder on the way to it, so the result reads as a tree of the volume instead of one long path
/// per row.
/// </summary>
public sealed class ResultTreeNode : INotifyPropertyChanged
{
    private readonly List<ResultTreeNode> _children = new();
    private bool _isExpanded = true;

    internal ResultTreeNode(string name, string fullPath, int depth)
    {
        Name = name;
        FullPath = fullPath;
        Depth = depth;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The last segment of the path, which is what the tree shows.</summary>
    public string Name { get; }

    /// <summary>The whole path of the folder this node stands for.</summary>
    public string FullPath { get; }

    /// <summary>How deep the folder sits below the root of the path, which is the indent of the row.</summary>
    public int Depth { get; }

    public IReadOnlyList<ResultTreeNode> Children => _children;

    public bool HasChildren => _children.Count > 0;

    /// <summary>The folder itself, when it matched the rules. Ancestors of a match have none.</summary>
    public FolderResult? Result { get; private set; }

    /// <summary>True when this folder matched the rules, false for the folders on the way to it.</summary>
    public bool IsMatch => Result is not null;

    /// <summary>
    /// The note about this folder. It is kept on the folder itself so it travels with the report,
    /// and setting it here also tells the list that the row changed.
    /// </summary>
    public string Comment
    {
        get => Result?.Comment ?? string.Empty;
        set
        {
            if (Result is null || string.Equals(Result.Comment, value, StringComparison.Ordinal))
            {
                return;
            }

            Result.Comment = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Comment)));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public string SizeText => Result?.SizeText ?? string.Empty;

    public string FilesText => Result?.FilesText ?? string.Empty;

    public string SubfolderText => Result?.SubfolderText ?? string.Empty;

    public string DetailText => Result is null
        ? $"{FullPath}{Environment.NewLine}Shown to place the matches below it. This folder did not match the rules."
        : $"{FullPath}{Environment.NewLine}{Result.DetailText}";

    internal void AddChild(ResultTreeNode child) => _children.Add(child);

    internal void MarkMatched(FolderResult result) => Result = result;

    /// <summary>Sorts this folder's children and everything below them by name.</summary>
    internal void SortRecursively()
    {
        _children.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

        foreach (ResultTreeNode child in _children)
        {
            child.SortRecursively();
        }
    }
}

/// <summary>Turns a flat list of matched folders into a tree of nodes, ordered by full path.</summary>
public static class ResultTree
{
    /// <summary>
    /// Builds the tree. The folders are looked at in path order, so every folder sits under its
    /// parent and every folder's children read from A to Z.
    /// </summary>
    public static IReadOnlyList<ResultTreeNode> Build(IEnumerable<FolderResult> results)
    {
        var roots = new List<ResultTreeNode>();
        var nodes = new Dictionary<string, ResultTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (FolderResult result in results.OrderBy(folder => folder.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyList<string> segments = SplitPath(result.FullPath);
            if (segments.Count == 0)
            {
                continue;
            }

            ResultTreeNode? parent = null;
            string path = string.Empty;

            for (int index = 0; index < segments.Count; index++)
            {
                path = index == 0 ? segments[0] : Join(path, segments[index]);

                if (!nodes.TryGetValue(path, out ResultTreeNode? node))
                {
                    node = new ResultTreeNode(segments[index], path, index);
                    nodes[path] = node;

                    if (parent is null)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        parent.AddChild(node);
                    }
                }

                parent = node;
            }

            parent?.MarkMatched(result);
        }

        Sort(roots);
        return roots;
    }

    /// <summary>
    /// The rows a list should show right now: every node whose parents are all expanded, in the
    /// same order as the tree. Collapsed folders hide everything below them.
    /// </summary>
    public static IReadOnlyList<ResultTreeNode> Visible(IReadOnlyList<ResultTreeNode> roots)
    {
        var visible = new List<ResultTreeNode>();
        AddVisible(roots, visible);
        return visible;
    }

    /// <summary>Every node of the tree, in path order, whether it is expanded or not.</summary>
    public static IReadOnlyList<ResultTreeNode> All(IReadOnlyList<ResultTreeNode> roots)
    {
        var all = new List<ResultTreeNode>();
        AddAll(roots, all);
        return all;
    }

    /// <summary>
    /// Splits a path into the parts the tree shows as rows: the root ("D:\", "\", "\\server\share\")
    /// first, then one part per folder below it.
    /// </summary>
    public static IReadOnlyList<string> SplitPath(string fullPath)
    {
        var segments = new List<string>();
        string path = (fullPath ?? string.Empty).Trim();

        if (path.Length == 0)
        {
            return segments;
        }

        int start = 0;

        if (path.Length >= 2 && path[1] == ':')
        {
            segments.Add(path[..2] + "\\");
            start = 2;
        }
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // A network path: \\server\share is the root, everything below it are folders.
            int shareEnd = path.IndexOf('\\', 2);
            shareEnd = shareEnd < 0 ? -1 : path.IndexOf('\\', shareEnd + 1);

            string root = shareEnd > 0 ? path[..shareEnd] : path.TrimEnd('\\');
            if (root.Length == 0)
            {
                return segments;
            }

            segments.Add(root + "\\");
            start = root.Length;
        }
        else if (path[0] == '\\' || path[0] == '/')
        {
            segments.Add(path[..1]);
            start = 1;
        }

        foreach (string part in path[start..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            segments.Add(part);
        }

        return segments;
    }

    private static string Join(string parent, string child)
    {
        char separator = parent.EndsWith('/') ? '/' : '\\';
        return parent.EndsWith(separator) ? parent + child : parent + separator + child;
    }

    private static void Sort(IEnumerable<ResultTreeNode> nodes)
    {
        foreach (ResultTreeNode node in nodes)
        {
            node.SortRecursively();
        }
    }

    private static void AddVisible(IReadOnlyList<ResultTreeNode> nodes, List<ResultTreeNode> visible)
    {
        foreach (ResultTreeNode node in nodes)
        {
            visible.Add(node);

            if (node.IsExpanded)
            {
                AddVisible(node.Children, visible);
            }
        }
    }

    private static void AddAll(IReadOnlyList<ResultTreeNode> nodes, List<ResultTreeNode> all)
    {
        foreach (ResultTreeNode node in nodes)
        {
            all.Add(node);
            AddAll(node.Children, all);
        }
    }
}
