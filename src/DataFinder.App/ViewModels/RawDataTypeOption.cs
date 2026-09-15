using DataFinder.Core.Preview.Raw;

namespace DataFinder.App.ViewModels;

/// <summary>One entry of the "data type" drop down: the layout, and how to say it in the window.</summary>
public sealed record RawDataTypeOption(RawDataType DataType, string Text)
{
    /// <summary>Every layout, in the order the drop down shows them.</summary>
    public static IReadOnlyList<RawDataTypeOption> All { get; } = RawDataTypes.All
        .Select(dataType => new RawDataTypeOption(dataType, RawDataTypes.Describe(dataType)))
        .ToList();
}
