using DataFinder.Core.Models;

namespace DataFinder.Core.Volumes;

/// <summary>Lists the mounted volumes that can be scanned.</summary>
public static class VolumeEnumerator
{
    public static IReadOnlyList<VolumeInfo> GetNtfsVolumes()
    {
        var volumes = new List<VolumeInfo>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                {
                    continue;
                }

                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string root = drive.RootDirectory.FullName;
                if (root.Length == 0)
                {
                    continue;
                }

                volumes.Add(new VolumeInfo
                {
                    DriveLetter = char.ToUpperInvariant(root[0]),
                    RootPath = root,
                    Label = drive.VolumeLabel ?? string.Empty,
                    FileSystem = drive.DriveFormat,
                    TotalSizeBytes = drive.TotalSize,
                    FreeSpaceBytes = drive.AvailableFreeSpace,
                });
            }
            catch (IOException)
            {
                // A drive that disappeared between enumeration and inspection is simply skipped.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return volumes;
    }
}

