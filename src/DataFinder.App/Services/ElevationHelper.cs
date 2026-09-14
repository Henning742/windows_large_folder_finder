using System.Diagnostics;

namespace DataFinder.App.Services;

/// <summary>Reading a raw NTFS volume requires administrator rights, so the app checks for them.</summary>
public static class ElevationHelper
{
    public static bool IsElevated()
    {
        try
        {
            return Environment.IsPrivilegedProcess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Starts a second copy of the app through the UAC prompt. Returns false when the user declines.</summary>
    public static bool RelaunchElevated()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

