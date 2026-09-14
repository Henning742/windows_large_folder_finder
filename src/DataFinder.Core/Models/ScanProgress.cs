namespace DataFinder.Core.Models;

public sealed record ScanProgress(string Stage, long ItemsProcessed, long TotalItems, int FoldersFound)
{
    public double Fraction => TotalItems > 0
        ? Math.Clamp((double)ItemsProcessed / TotalItems, 0d, 1d)
        : 0d;
}

