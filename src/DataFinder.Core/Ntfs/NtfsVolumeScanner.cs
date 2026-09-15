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

    private readonly Func<char, IRawVolumeReader> _openVolume;

    public NtfsVolumeScanner()
        : this(RawVolumeStream.Open)
    {
    }

    /// <summary>
    /// Takes the volume from somewhere else. This is how a scan is run without a real drive: the
    /// tests hand over a volume built in memory, which is also the only way to try out what happens
    /// when part of the master file table cannot be read.
    /// </summary>
    public NtfsVolumeScanner(Func<char, IRawVolumeReader> openVolume) =>
        _openVolume = openVolume ?? throw new ArgumentNullException(nameof(openVolume));

    public ScanReport Scan(
        VolumeInfo volume,
        ScanSettings settings,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>();

        using IRawVolumeReader raw = _openVolume(volume.DriveLetter);

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

        // The $MFT's own $ATTRIBUTE_LIST can continue in extension records, so the whole table is
        // resolved before it is read instead of stopping at the end of the first extent.
        List<DataRun> runs = MftExtentResolver.Resolve(
            raw, bootSector.BytesPerCluster, bootSector.BytesPerSector, recordSize, mftRecord);
        mftDataSize = Math.Max(mftDataSize, mftRecord.DataSize);

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
        long recordsInHoles = 0;
        long unreadableRecords = 0;
        long stoppedAt = -1;
        bool stoppedByHole = false;
        bool mftReadIncomplete = false;

        using (var reader = new MftRecordReader(raw, bootSector.BytesPerCluster, recordSize, runs, mftDataSize))
        {
            var expander = new MftRecordExpander(
                reader,
                bootSector.BytesPerSector,
                recordSize,
                readAttributeContent: MftExtentResolver.AttributeContent(raw, bootSector.BytesPerCluster));

            long recordNumber = 0;

            while (recordNumber < recordCount)
            {
                if ((recordNumber & CancellationCheckMask) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // Report for every tick of the loop, whatever the record turns out to be, so the
                // bar keeps moving even while unreadable regions are being skipped.
                if (progress is not null && recordNumber % RecordsPerProgressReport == 0)
                {
                    progress.Report(new ScanProgress(
                        "Reading the master file table",
                        recordNumber,
                        recordCount,
                        index.DirectoryCount));
                }

                MftRecordRead read = reader.ReadRecord(recordNumber, recordBuffer);

                if (read == MftRecordRead.BeyondTable)
                {
                    break;
                }

                if (read != MftRecordRead.Success)
                {
                    // A hole in the run list, or a place the volume will not read, is not the end of
                    // the table: find the next record there is anything to read and carry on, so
                    // whatever sits after the damage is still found. Going round forever is not
                    // possible - the record number only ever moves forward.
                    mftReadIncomplete = true;
                    bool hole = read == MftRecordRead.NotCovered;
                    long next = hole
                        ? reader.FindNextCoveredRecord(recordNumber + 1)
                        : reader.FindNextReadableRecord(recordNumber, recordCount);

                    if (next < 0 || next >= recordCount)
                    {
                        stoppedAt = recordNumber;
                        stoppedByHole = hole;
                        break;
                    }

                    if (hole)
                    {
                        recordsInHoles += next - recordNumber;
                    }
                    else
                    {
                        unreadableRecords += next - recordNumber;
                    }

                    recordNumber = next;
                    continue;
                }

                recordsRead++;

                MftRecordParseResult? parsed = parser.Parse(recordBuffer, bootSector.BytesPerSector, (uint)recordNumber);
                if (parsed is null)
                {
                    recordNumber++;
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

                recordNumber++;
            }
        }

        if (stoppedAt >= 0)
        {
            // Where the scan has to stop says which of the two it was: a run list that stops short, or
            // a volume that will not hand over the bytes its run list points at.
            warnings.Add(stoppedByHole
                ? $"The data runs of the master file table stop at record {stoppedAt:N0} of {recordCount:N0}, " +
                  "so the records after that were not read."
                : $"The master file table could not be read past record {stoppedAt:N0} of {recordCount:N0}, " +
                  "so folders stored after that are missing from the results.");
        }

        if (recordsInHoles > 0)
        {
            warnings.Add(
                $"{recordsInHoles:N0} records fall in a hole between the data runs of the master file table and were skipped.");
        }

        if (unreadableRecords > 0)
        {
            warnings.Add(
                $"{unreadableRecords:N0} records could not be read from the volume and were skipped, " +
                "so folders stored in them are missing from the results.");
        }

        if (unresolvedAttributeLists > 0)
        {
            warnings.Add(
                $"{unresolvedAttributeLists:N0} files could not be fully read because their $ATTRIBUTE_LIST pointed at records that were missing or damaged.");
        }

        progress?.Report(new ScanProgress("Applying the rules", Math.Min(recordsRead, recordCount), Math.Max(recordCount, 1), index.DirectoryCount));

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
            ExpectedRecordCount = recordCount,
            RecordsInUse = recordsInUse,
            MftReadCompleted = !mftReadIncomplete,
            Warnings = warnings,
        };
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
