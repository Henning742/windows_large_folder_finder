using System.Buffers.Binary;
using DataFinder.Core.Preview.Raw;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class RawFrameReaderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));

    public RawFrameReaderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void SkipsTheHeaderAndReadsOnlyTheFrame()
    {
        string file = WriteRecording(header: 4, frames: new[] { new byte[] { 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 } });
        RawSchema schema = Grey(2, 2, header: 4);

        RawReadResult read = RawFrameReader.Read(file, schema);

        Assert.True(read.Succeeded);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, read.Bytes!);
    }

    [Fact]
    public void ReadsTheFrameTheSchematicAsksFor()
    {
        string file = WriteRecording(header: 4, frames: new[] { new byte[] { 1, 2, 3, 4 }, new byte[] { 5, 6, 7, 8 } });
        RawSchema schema = Grey(2, 2, header: 4);
        schema.FrameIndex = 1;

        Assert.Equal(new byte[] { 5, 6, 7, 8 }, RawFrameReader.Read(file, schema).Bytes!);
    }

    [Fact]
    public void ReadsAFileWithNoHeader()
    {
        string file = WriteRecording(header: 0, frames: new[] { new byte[] { 9, 8, 7, 6 } });

        Assert.Equal(new byte[] { 9, 8, 7, 6 }, RawFrameReader.Read(file, Grey(2, 2, 0)).Bytes!);
    }

    [Fact]
    public void SaysHowShortTheFileIsWhenTheFrameIsNotThere()
    {
        string file = WriteRecording(header: 0, frames: new[] { new byte[] { 1, 2, 3, 4 } });

        RawReadResult read = RawFrameReader.Read(file, Grey(640, 512, 0));

        Assert.False(read.Succeeded);
        Assert.Contains("only", read.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysWhichFrameIsMissingWhenItIsNotTheFirst()
    {
        string file = WriteRecording(header: 0, frames: new[] { new byte[] { 1, 2, 3, 4 } });
        RawSchema schema = Grey(2, 2, 0);
        schema.FrameIndex = 7;

        RawReadResult read = RawFrameReader.Read(file, schema);

        Assert.False(read.Succeeded);
        Assert.Contains("frame 7", read.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWithAReasonWhenTheFileIsNotThere()
    {
        RawReadResult read = RawFrameReader.Read(Path.Combine(_folder, "missing.dat"), Grey(2, 2, 0));

        Assert.False(read.Succeeded);
        Assert.NotNull(read.Error);
    }

    [Fact]
    public void ReadsAndDecodesInOneStep()
    {
        string file = WriteRecording(header: 0, frames: new[] { new byte[] { 0, 128, 255, 64 } });

        RawDecodeResult result = RawFrameReader.Decode(file, Grey(2, 2, 0));

        Assert.True(result.Succeeded);
        Assert.Equal(new byte[] { 0, 128, 255, 64 }, result.Frame!.Pixels);
    }

    private static RawSchema Grey(int width, int height, int header) => new()
    {
        Name = "test",
        Width = width,
        Height = height,
        HeaderLength = header,
        DataType = RawDataType.U8,
    };

    private string WriteRecording(int header, IReadOnlyList<byte[]> frames)
    {
        string path = Path.Combine(_folder, Path.GetRandomFileName() + ".dat");

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        foreach (byte[] frame in frames)
        {
            for (int i = 0; i < header; i++)
            {
                stream.WriteByte((byte)i);
            }

            stream.Write(frame);
        }

        return path;
    }
}
