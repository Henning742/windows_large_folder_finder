namespace DataFinder.Core.Ntfs;

/// <summary>A failure that can be explained to the user without any extra context.</summary>
public sealed class NtfsScanException : Exception
{
    public NtfsScanException(string message)
        : base(message)
    {
    }

    public NtfsScanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

