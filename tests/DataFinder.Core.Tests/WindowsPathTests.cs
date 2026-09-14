using DataFinder.Core.Util;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class WindowsPathTests
{
    [Theory]
    [InlineData(@"D:\", "data", @"D:\data")]
    [InlineData(@"D:\projects", "data", @"D:\projects\data")]
    [InlineData("", "data", "data")]
    public void CombinesPathsWithBackslashes(string parent, string child, string expected)
    {
        Assert.Equal(expected, WindowsPath.Combine(parent, child));
    }

    [Theory]
    [InlineData("D:", @"D:\")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("D:/", @"D:\")]
    public void NormalizesVolumeRoots(string root, string expected)
    {
        Assert.Equal(expected, WindowsPath.NormalizeRoot(root));
    }
}

