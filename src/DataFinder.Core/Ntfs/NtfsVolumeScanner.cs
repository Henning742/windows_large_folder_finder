using System.Diagnostics;
using DataFinder.Core.Models;

namespace DataFinder.Core.Ntfs;

/// <summary>
/// Reads the master file table of an NTFS volume and applies the two rules the user configured.
/// Needs administrator rights because it opens the raw volume device.
/// </summary>
public sealed class NtfsVolumeScanner
{
    private const int RecordsPerProgressReport = 4096;
    private const int CancellationCheckMask = 0x3FF;

    public ScanReport Scan(
        VolumeInfo volume,
        ScanSettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        using RawVolumeStream raw = RawVolumeStream.Open(volume.DriveLetter);

        if (!NtfsBootSector.TryParse(raw.ReadBootSectorProbe(), out NtfsBootSector? bootSector, out string? bootError) ||
            bootSector is null)
        {
            throw new NtfsScanException(bootError ?? $"The boot sector of {volume.DriveLetter}: could not be read.");
        }

        raw.Configure(bootSector.BytesPerSector, bootSector.VolumeSizeBytes);

        int recordSize = bootSector.MftRecordSize;
        var parser = new MftRecordParser();
        var firstRecord = new byte[recordSize];

        if (raw.ReadAligned(bootSector.MftStartByteOffset, firstRecord, 0, recordSize) < recordSize)
        {
            throw new NtfsScanException("The master file table could not be read from this volume.");
        }

        MftRecordParseResult? mftRecord = parser.Parse(firstRecord, bootSector.BytesPerSector, 0, captureDataRunlist: true);
        if (mftRecord is null || !mftRecord.HasData)
        {
            throw new NtfsScanException(
                "The $MFT record could not be read. The volume may be encrypted (BitLocker) or damaged.");
        }

        long mftDataSize = mftRecord.DataSize;
        bool mftHasAttributeList = mftRecord.HasAttributeList;

        List<DataRun> runs = Runlist.DecodeExtents(mftRecord.DataExtents);
        if (runs.Count == 0)
        {
            if (mftDataSize <= 0)
            {
                throw new NtfsScanException("The master file table does not expose any readable data runs.");
            }

            long clusterCount = (mftDataSize + bootSector.BytesPerCluster - 1) / bootSector.BytesPerCluster;
            runs.Add(new DataRun(0, clusterCount, bootSector.MftStartCluster));
            warnings.Add(
                "The $MFT data runs could not be decoded, so the table was read as a single contiguous block. " +
                "On a heavily fragmented volume some folders may be missing.");
        }
        else if (mftHasAttributeList)
        {
            // The $MFT's own $DATA attribute can continue in extension records. Read them so the
            // whole table is available instead of stopping at the end of the first extent.
            int knownExtents = mftRecord.DataExtents.Count;
            ResolveMftExtents(raw, bootSector, recordSize, runs, mftDataSize, mftRecord);

            if (mftRecord.DataExtents.Count > knownExtents)
            {
                runs = Runlist.DecodeExtents(mftRecord.DataExtents);
                mftDataSize = Math.Max(mftDataSize, mftRecord.DataSize);
            }
        }

        if (mftHasAttributeList && !RunlistCoversTable(runs, bootSector.BytesPerCluster, mftDataSize))
        {
            warnings.Add(
                "The $MFT uses an $ATTRIBUTE_LIST that could not be fully resolved, so a few folders may be missing from the results.");
        }

        long recordCount = mftDataSize / recordSize;
        var index = new MftIndex();
        var recordBuffer = new byte[recordSize];
        long recordsRead = 0;
        long recordsInUse = 0;
        long unresolvedAttributeLists = 0;

        using (var reader = new MftRecordReader(raw, bootSector.BytesPerCluster, recordSize, runs, mftDataSize))
        {
            var expander = new MftRecordExpander(reader, bootSector.BytesPerSector, recordSize);

            for (long recordNumber = 0; recordNumber < recordCount; recordNumber++)
            {
                if ((recordNumber & CancellationCheckMask) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (!reader.TryGetRecord(recordNumber, recordBuffer))
                {
                    warnings.Add($"The master file table ended early, at record {recordNumber:N0} of {recordCount:N0}.");
                    break;
                }

                MftRecordParseResult? parsed = parser.Parse(recordBuffer, bootSector.BytesPerSector, (uint)recordNumber);
                if (parsed is null)
                {
                    continue;
                }

                if (parsed.InUse)
                {
                    recordsInUse++;

                    // A file whose attributes spilled into extension records is completed here,
                    // before it is added, so the whole file is measured as one.
                    if (parsed.HasAttributeList && !parsed.IsExtensionRecord && !expander.Expand(parsed))
                    {
                        unresolvedAttributeLists++;
                    }

                    index.Add(parsed);
                }

                recordsRead = recordNumber + 1;

                if (progress is not null && recordNumber % RecordsPerProgressReport == 0)
                {
                    progress.Report(new ScanProgress(
                        "Reading the master file table",
                        recordNumber,
                        recordCount,
                        index.DirectoryCount));
                }
            }
        }

        if (unresolvedAttributeLists > 0)
        {
            warnings.Add(
                $"{unresolvedAttributeLists:N0} files could not be fully read because their $ATTRIBUTE_LIST pointed at records that were missing or damaged.");
        }

        progress?.Report(new ScanProgress("Applying the rules", recordsRead, Math.Max(recordCount, 1), index.DirectoryCount));

        AggregationResult aggregation = index.Build(settings, volume.RootPath);
        warnings.AddRange(aggregation.Warnings);

        stopwatch.Stop();

        return new ScanReport
        {
            Volume = volume,
            Settings = settings,
            Results = aggregation.Results,
            Index = index,
            Aggregation = aggregation,
            Elapsed = stopwatch.Elapsed,
            RecordsRead = recordsRead,
            RecordsInUse = recordsInUse,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Reads the extension records referenced by the <c>$MFT</c>'s own <c>$ATTRIBUTE_LIST</c> and
    /// merges their <c>$DATA</c> run lists into the record, so a fragmented master file table can
    /// be read in full. Records are read through the runs found so far; the extension records of
    /// <c>$MFT</c> always live early enough in the table to be reachable.
    /// </summary>
    private static void ResolveMftExtents(
        RawVolumeStream raw,
        NtfsBootSector bootSector,
        int recordSize,
        IReadOnlyList<DataRun> runs,
        long mftDataSize,
        MftRecordParseResult mftRecord)
    {
        try
        {
            using var reader = new MftRecordReader(raw, bootSector.BytesPerCluster, recordSize, runs, mftDataSize);
            var expander = new MftRecordExpander(reader, bootSector.BytesPerSector, recordSize, captureDataRunlists: true);
            expander.Expand(mftRecord);
        }
        catch (NtfsScanException)
        {
            // A malformed list or run list simply leaves the runs as they were.
        }
    }

    /// <summary>True when the runs reach the very end of the attribute they describe.</summary>
    private static bool RunlistCoversTable(IReadOnlyList<DataRun> runs, int bytesPerCluster, long dataSize)
    {
        if (dataSize <= 0)
        {
            return true;
        }

        long coveredBytes = 0;
        foreach (DataRun run in runs)
        {
            long end = run.EndVcn * bytesPerCluster;
            if (end > coveredBytes)
            {
                coveredBytes = end;
            }
        }

        return coveredBytes >= dataSize;
    }
}
