namespace DataFinder.Core.Preview;

public enum PreviewKind
{
    None = 0,
    Image = 1,
    Text = 2,

    /// <summary>A data file that the decoder reads with the schematics set up in the window.</summary>
    Binary = 3,
}
