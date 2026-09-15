using DataFinder.Core.Models;

namespace DataFinder.Core.Util;

/// <summary>
/// Guesses how much longer a job will take from how far it has got and how long it has been
/// running. The first reports of a scan are the least reliable, so the estimate only appears once
/// the job is really under way, and it is averaged with the previous one so the number settles
/// instead of jumping around.
/// </summary>
public sealed class RemainingTimeEstimator
{
    /// <summary>Below this share of the job the rate says more about starting up than about the work.</summary>
    private const double MinimumFraction = 0.005;

    /// <summary>How much a new measurement moves the estimate: low enough to stay calm.</summary>
    private const double Smoothing = 0.3;

    private TimeSpan? _estimate;

    public TimeSpan? Estimate => _estimate;

    /// <summary>
    /// Takes the current position, from 0 to 1, and how long the job has been running. Returns the
    /// estimate, or null while it is still too early to say.
    /// </summary>
    public TimeSpan? Update(double fraction, TimeSpan elapsed)
    {
        if (double.IsNaN(fraction) || elapsed < TimeSpan.Zero)
        {
            return _estimate;
        }

        if (fraction >= 1d)
        {
            _estimate = TimeSpan.Zero;
            return _estimate;
        }

        if (fraction < MinimumFraction || elapsed <= TimeSpan.Zero)
        {
            return _estimate;
        }

        double remainingSeconds = elapsed.TotalSeconds * (1d - fraction) / fraction;
        var measurement = TimeSpan.FromSeconds(Math.Max(0d, remainingSeconds));

        _estimate = _estimate is null || _estimate.Value <= TimeSpan.Zero
            ? measurement
            : TimeSpan.FromSeconds((_estimate.Value.TotalSeconds * (1d - Smoothing)) + (measurement.TotalSeconds * Smoothing));

        return _estimate;
    }

    public void Reset() => _estimate = null;
}

/// <summary>
/// Estimates the remaining time of a run that reads several volumes one after the other. Each
/// volume gets a share of the job in proportion to its size, which is the closest thing to the
/// size of its master file table that is known before the scan starts.
/// </summary>
public sealed class ScanTimeEstimator
{
    private readonly List<double> _weights = new();
    private readonly RemainingTimeEstimator _remaining = new();

    public ScanTimeEstimator(IEnumerable<VolumeInfo> volumes)
    {
        foreach (VolumeInfo volume in volumes)
        {
            _weights.Add(Math.Max(volume.TotalSizeBytes, 1d));
        }
    }

    public TimeSpan? Estimate => _remaining.Estimate;

    /// <summary>How much of the whole run is done: the volumes already read plus the share of this one.</summary>
    public double Fraction(int volumeIndex, double fractionOfVolume)
    {
        if (_weights.Count == 0)
        {
            return 0d;
        }

        double total = _weights.Sum();
        double done = 0d;

        for (int index = 0; index < _weights.Count; index++)
        {
            if (index < volumeIndex)
            {
                done += _weights[index];
            }
            else if (index == volumeIndex)
            {
                done += _weights[index] * Math.Clamp(fractionOfVolume, 0d, 1d);
            }
        }

        return total <= 0d ? 0d : Math.Clamp(done / total, 0d, 1d);
    }

    /// <summary>Feeds the position of the volume being read, and answers how much longer it will all take.</summary>
    public TimeSpan? Update(int volumeIndex, double fractionOfVolume, TimeSpan elapsed) =>
        _remaining.Update(Fraction(volumeIndex, fractionOfVolume), elapsed);

    public void Reset() => _remaining.Reset();
}

/// <summary>Turns a time span into the short form the status bar has room for.</summary>
public static class DurationText
{
    public static string Format(TimeSpan duration)
    {
        double seconds = Math.Max(0d, duration.TotalSeconds);

        // Rounded up, so a job that still has work to do never reads "0 s left".
        if (seconds < 60d)
        {
            return $"{Math.Ceiling(seconds):0} s";
        }

        if (seconds < 3600d)
        {
            int minutes = (int)(seconds / 60d);
            int rest = (int)Math.Round(seconds - (minutes * 60d));

            if (rest == 60)
            {
                minutes++;
                rest = 0;
            }

            return rest == 0 ? $"{minutes} min" : $"{minutes} min {rest} s";
        }

        int hours = (int)(seconds / 3600d);
        int minutesLeft = (int)Math.Round((seconds - (hours * 3600d)) / 60d);

        if (minutesLeft == 60)
        {
            hours++;
            minutesLeft = 0;
        }

        return minutesLeft == 0 ? $"{hours} h" : $"{hours} h {minutesLeft} min";
    }
}
