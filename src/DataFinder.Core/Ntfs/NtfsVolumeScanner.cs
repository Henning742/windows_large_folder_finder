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

        // Copy what is needed out of the record: the parser reuses the same object for every record.
        byte[]? runlist = mftRecord.DataRunlist;
        long mftDataSize = mftRecord.DataSize;
        bool mftHasAttributeList = mftRecord.HasAttributeList;

        if (mftDataSize <= 0 || runlist is null)
        {
            throw new NtfsScanException("The master file table does not expose any readable data runs.");
        }

        List<DataRun> runs = Runlist.Decode(runlist);
        if (runs.Count == 0)
        {
            long clusterCount = (mftDataSize + bootSector.BytesPerCluster - 1) / bootSector.BytesPerCluster;
            runs.Add(new DataRun(0, clusterCount, bootSector.MftStartCluster));
            warnings.Add(
                "The $MFT data runs could not be decoded, so the table was read as a single contiguous block. " +
                "On a heavily fragmented volume some folders may be missing.");
        }

        if (mftHasAttributeList)
        {
            warnings.Add(
                "The $MFT uses an $ATTRIBUTE_LIST. If the master file table itself is fragmented, a few folders may be missing from the results.");
        }

        long recordCount = mftDataSize / recordSize;
        var index = new MftIndex();
        var recordBuffer = new byte[recordSize];
        long recordsRead = 0;
        long recordsInUse = 0;

        using (var reader = new MftRecordReader(raw, bootSector.BytesPerCluster, recordSize, runs, mftDataSize))
        {
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
}

