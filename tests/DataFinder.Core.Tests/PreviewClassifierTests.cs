using DataFinder.Core.Preview;
using DataFinder.Core.Preview.Raw;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class PreviewClassifierTests
{
    [Fact]
    public void KnowsImagesAndText()
    {
        Assert.Equal(PreviewKind.Image, PreviewClassifier.Classify(@"D:\data\shot.png"));
        Assert.Equal(PreviewKind.Text, PreviewClassifier.Classify(@"D:\data\notes.txt"));
        Assert.Equal(PreviewKind.None, PreviewClassifier.Classify(@"D:\data\thing.dll"));
    }

    [Fact]
    public void OffersListedSuffixesToTheDecoder()
    {
        Assert.Equal(PreviewKind.Binary, PreviewClassifier.Classify(@"D:\data\frame0001.dat", RawFileTypes.Default));
        Assert.Equal(PreviewKind.Binary, PreviewClassifier.Classify(@"D:\data\frame0001.RAW", RawFileTypes.Default));
    }

    [Fact]
    public void LeavesSuffixesAloneThatSomethingElseAlreadyKnows()
    {
        // Listing .csv for decoding should not take the text preview away from a .csv file.
        Assert.Equal(PreviewKind.Text, PreviewClassifier.Classify(@"D:\data\list.csv", new[] { ".csv" }));
        Assert.Equal(PreviewKind.Image, PreviewClassifier.Classify(@"D:\data\shot.jpg", new[] { ".jpg" }));
    }

    [Fact]
    public void HasNoDecoderWhenTheSuffixListIsEmpty()
    {
        Assert.Equal(PreviewKind.None, PreviewClassifier.Classify(@"D:\data\frame0001.dat"));
        Assert.Equal(PreviewKind.None, PreviewClassifier.Classify(@"D:\data\frame0001.dat", Array.Empty<string>()));
    }

    [Fact]
    public void ReadsTheExtensionOffAPathOrAName()
    {
        Assert.Equal(".dat", PreviewClassifier.ExtensionOf(@"D:\data\frame0001.DAT"));
        Assert.Equal(".dat", PreviewClassifier.ExtensionOf("frame0001.dat"));
        Assert.Equal(string.Empty, PreviewClassifier.ExtensionOf("frame0001"));
        Assert.Equal(string.Empty, PreviewClassifier.ExtensionOf(@"D:\data.d\frame0001"));
        Assert.Equal(string.Empty, PreviewClassifier.ExtensionOf(string.Empty));
    }

    [Fact]
    public void KnowsTheSuffixesOfTheReferenceScriptsOutOfTheBox()
    {
        Assert.True(RawFileTypes.Matches(RawFileTypes.Default, "a.raw"));
        Assert.True(RawFileTypes.Matches(RawFileTypes.Default, "a.bin"));
        Assert.True(RawFileTypes.Matches(RawFileTypes.Default, "a.data"));
    }
}
