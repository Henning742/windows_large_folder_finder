namespace DataFinder.Core.Util;

/// <summary>
/// Path helpers that always produce Windows style paths, so the scanning logic behaves the
/// same no matter which operating system the tests run on.
/// </summary>
public static class WindowsPath
{
    public static string Combine(string parent, string child)
    {
        if (string.IsNullOrEmpty(parent))
        {
            return child;
        }

        if (string.IsNullOrEmpty(child))
        {
            return parent;
        }

        return parent.EndsWith('\\') ? parent + child : parent + "\\" + child;
    }

    public static string NormalizeRoot(string root)
    {
        string trimmed = (root ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "\\";
        }

        if (trimmed.EndsWith(':'))
        {
            return trimmed + "\\";
        }

        return trimmed.EndsWith('\\') || trimmed.EndsWith('/') ? trimmed[..^1] + "\\" : trimmed + "\\";
    }
}

