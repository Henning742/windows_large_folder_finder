using System.Diagnostics;
using System.Windows;

namespace DataFinder.App.Services;

/// <summary>Hands a path over to Windows Explorer or to the default application for a file.</summary>
public static class ShellService
{
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Explorer not being available is not worth interrupting the user for.
        }
    }

    public static void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
        }
    }

    public static bool TryCopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

