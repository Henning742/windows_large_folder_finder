namespace DataFinder.Core.Ntfs;

/// <summary>Decodes an NTFS data run list into absolute cluster runs.</summary>
public static class Runlist
{
    public static List<DataRun> Decode(ReadOnlySpan<byte> runList)
    {
        var runs = new List<DataRun>();
        long currentVcn = 0;
        long currentLcn = 0;
        int position = 0;

        while (position < runList.Length)
        {
            byte header = runList[position++];
            if (header == 0)
            {
                break;
            }

            int lengthBytes = header & 0x0F;
            int offsetBytes = header >> 4;

            if (lengthBytes == 0 || lengthBytes > 8 || offsetBytes > 8)
            {
                throw new NtfsScanException("The volume contains a malformed data run list.");
            }

            if (position + lengthBytes + offsetBytes > runList.Length)
            {
                break;
            }

            long clusterCount = ReadUnsigned(runList.Slice(position, lengthBytes));
            position += lengthBytes;

            if (clusterCount <= 0)
            {
                break;
            }

            if (offsetBytes == 0)
            {
                runs.Add(new DataRun(currentVcn, clusterCount, -1));
            }
            else
            {
                currentLcn += ReadSigned(runList.Slice(position, offsetBytes));
                position += offsetBytes;
                runs.Add(new DataRun(currentVcn, clusterCount, currentLcn));
            }

            currentVcn += clusterCount;
        }

        return runs;
    }

    public static long TotalClusters(IReadOnlyList<DataRun> runs)
    {
        long total = 0;
        foreach (DataRun run in runs)
        {
            total += run.ClusterCount;
        }

        return total;
    }

    private static long ReadUnsigned(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            value = (value << 8) | bytes[i];
        }

        return value;
    }

    private static long ReadSigned(ReadOnlySpan<byte> bytes)
    {
        long value = ReadUnsigned(bytes);

        // Sign extend when the most significant byte has its top bit set.
        int bits = bytes.Length * 8;
        if (bits < 64 && (value & (1L << (bits - 1))) != 0)
        {
            value -= 1L << bits;
        }

        return value;
    }
}

