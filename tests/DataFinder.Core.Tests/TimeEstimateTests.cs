using DataFinder.Core.Models;
using DataFinder.Core.Util;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class TimeEstimateTests
{
    private const long Gigabyte = 1024L * 1024L * 1024L;

    [Fact]
    public void SaysNothingUntilTheJobIsReallyUnderWay()
    {
        var estimator = new RemainingTimeEstimator();

        Assert.Null(estimator.Update(0d, TimeSpan.FromSeconds(5)));
        Assert.Null(estimator.Update(0.001, TimeSpan.FromSeconds(5)));
        Assert.Null(estimator.Update(0.1, TimeSpan.Zero));
    }

    [Fact]
    public void ExtrapolatesFromHowFarItHasGot()
    {
        var estimator = new RemainingTimeEstimator();

        TimeSpan? estimate = estimator.Update(0.1, TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(90), estimate);
    }

    [Fact]
    public void SettlesInsteadOfJumpingWithEveryReport()
    {
        var estimator = new RemainingTimeEstimator();

        estimator.Update(0.1, TimeSpan.FromSeconds(10));
        TimeSpan? settled = estimator.Update(0.2, TimeSpan.FromSeconds(20));

        // The straight reading would be 80 s; the estimate only moves part of the way there.
        Assert.True(settled > TimeSpan.FromSeconds(80));
        Assert.True(settled < TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void ReachesZeroWhenTheJobIsDone()
    {
        var estimator = new RemainingTimeEstimator();
        estimator.Update(0.5, TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.Zero, estimator.Update(1d, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void MakesTheEstimateLongerWhenTheWorkSlowsDown()
    {
        var estimator = new RemainingTimeEstimator();
        estimator.Update(0.2, TimeSpan.FromSeconds(10));

        TimeSpan? later = estimator.Update(0.3, TimeSpan.FromSeconds(30));

        Assert.True(later > TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ForgetsEverythingOnReset()
    {
        var estimator = new RemainingTimeEstimator();
        estimator.Update(0.5, TimeSpan.FromSeconds(10));

        estimator.Reset();

        Assert.Null(estimator.Estimate);
        Assert.Null(estimator.Update(0.001, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void SplitsTheRunBetweenTheDrivesByTheirSize()
    {
        var estimator = new ScanTimeEstimator(new[] { Volume('C', Gigabyte), Volume('D', 3 * Gigabyte) });

        Assert.Equal(0d, estimator.Fraction(0, 0d));
        Assert.Equal(0.25, estimator.Fraction(0, 1d));
        Assert.Equal(0.25, estimator.Fraction(1, 0d));
        Assert.Equal(0.625, estimator.Fraction(1, 0.5));
        Assert.Equal(1d, estimator.Fraction(1, 1d));
    }

    [Fact]
    public void WeighsDrivesWithNoSizeReportedEqually()
    {
        var estimator = new ScanTimeEstimator(new[] { Volume('C', 0), Volume('D', 0), Volume('E', 0) });

        Assert.Equal(0.5, estimator.Fraction(1, 0.5));
        Assert.Equal(2d / 3d, estimator.Fraction(2, 0d));
    }

    [Fact]
    public void EstimatesTheWholeRunAndNotJustTheDriveBeingRead()
    {
        var estimator = new ScanTimeEstimator(new[] { Volume('C', Gigabyte), Volume('D', Gigabyte) });

        TimeSpan? estimate = estimator.Update(0, 0.5, TimeSpan.FromSeconds(30));

        // Half of the first drive is a quarter of the run, so 30 s of work leaves 90 s.
        Assert.Equal(TimeSpan.FromSeconds(90), estimate);
    }

    [Fact]
    public void SaysNothingAboutARunWithNoDrives()
    {
        var estimator = new ScanTimeEstimator(Array.Empty<VolumeInfo>());

        Assert.Equal(0d, estimator.Fraction(0, 0.5));
        Assert.Null(estimator.Update(0, 0.5, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(0.4, "1 s")]
    [InlineData(45, "45 s")]
    [InlineData(59.2, "60 s")]
    [InlineData(60, "1 min")]
    [InlineData(200, "3 min 20 s")]
    [InlineData(3600, "1 h")]
    [InlineData(3900, "1 h 5 min")]
    [InlineData(7200, "2 h")]
    public void WritesDurationsTheWayTheStatusBarNeeds(double seconds, string expected)
    {
        Assert.Equal(expected, DurationText.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void NeverWritesANegativeDuration()
    {
        Assert.Equal("0 s", DurationText.Format(TimeSpan.FromSeconds(-5)));
    }

    private static VolumeInfo Volume(char drive, long size) => new()
    {
        DriveLetter = drive,
        RootPath = drive + @":\",
        TotalSizeBytes = size,
    };
}
