using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace WinCare.Native;

public sealed class FileSampleFingerprint
{
    public required string Path { get; init; }
    public long Length { get; init; }
    public int SampleBytes { get; init; }
    public required string FirstSha256 { get; init; }
    public required string LastSha256 { get; init; }
    public required string Fingerprint { get; init; }
}

public sealed class PortableExecutableSection
{
    public required string Name { get; init; }
    public int VirtualSize { get; init; }
    public int VirtualAddress { get; init; }
    public int RawDataSize { get; init; }
    public int RawDataPointer { get; init; }
    public required string Characteristics { get; init; }
}

public sealed class PortableExecutableReport
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public long Length { get; init; }
    public bool IsPortableExecutable { get; init; }
    public bool HasMetadata { get; init; }
    public bool IsManaged { get; init; }
    public string Machine { get; init; } = string.Empty;
    public string Subsystem { get; init; } = string.Empty;
    public string Characteristics { get; init; } = string.Empty;
    public double Entropy { get; init; }
    public IReadOnlyList<PortableExecutableSection> Sections { get; init; } = Array.Empty<PortableExecutableSection>();
}

public static class FileIntelligence
{
    private const int MaximumSampleBytes = 1_048_576;
    private const int MaximumSectionCount = 256;
    private const int BufferSize = 131_072;

    public static FileSampleFingerprint GetSampleFingerprint(
        string path,
        int sampleBytes = 65_536,
        long maximumFileBytes = 1_099_511_627_776L)
    {
        if (sampleBytes < 1_024 || sampleBytes > MaximumSampleBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleBytes));
        }

        FileInfo before = GetRegularFile(path, maximumFileBytes);
        long expectedLength = before.Length;
        long expectedWriteTicks = before.LastWriteTimeUtc.Ticks;

        using var stream = new FileStream(
            before.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);

        if (stream.Length != expectedLength)
        {
            throw new IOException("The file changed before fingerprinting began.");
        }

        int firstLength = checked((int)Math.Min(sampleBytes, expectedLength));
        byte[] first = new byte[firstLength];
        ReadExactly(stream, first);

        byte[] last;
        if (expectedLength <= sampleBytes)
        {
            last = first.ToArray();
        }
        else
        {
            stream.Position = expectedLength - sampleBytes;
            last = new byte[sampleBytes];
            ReadExactly(stream, last);
        }

        string firstHash;
        string lastHash;
        try
        {
            firstHash = Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant();
            lastHash = Convert.ToHexString(SHA256.HashData(last)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(first);
            CryptographicOperations.ZeroMemory(last);
        }

        FileInfo after = GetRegularFile(before.FullName, maximumFileBytes);
        if (after.Length != expectedLength || after.LastWriteTimeUtc.Ticks != expectedWriteTicks)
        {
            throw new IOException("The file changed while its fingerprint was computed.");
        }

        return new FileSampleFingerprint
        {
            Path = before.FullName,
            Length = expectedLength,
            SampleBytes = sampleBytes,
            FirstSha256 = firstHash,
            LastSha256 = lastHash,
            Fingerprint = $"{expectedLength}:{firstHash}:{lastHash}",
        };
    }

    public static PortableExecutableReport AnalyzePortableExecutable(
        string path,
        long maximumFileBytes = 1_073_741_824L,
        int maximumSections = 96)
    {
        if (maximumSections < 1 || maximumSections > MaximumSectionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSections));
        }

        FileInfo before = GetRegularFile(path, maximumFileBytes);
        long expectedLength = before.Length;
        long expectedWriteTicks = before.LastWriteTimeUtc.Ticks;

        string sha256;
        double entropy;
        using (var stream = new FileStream(
                   before.FullName,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   BufferSize,
                   FileOptions.SequentialScan))
        {
            (sha256, entropy) = HashAndMeasureEntropy(stream, expectedLength, maximumFileBytes);
        }

        var sections = new List<PortableExecutableSection>();
        bool isPortableExecutable = false;
        bool hasMetadata = false;
        bool isManaged = false;
        string machine = string.Empty;
        string subsystem = string.Empty;
        string characteristics = string.Empty;

        try
        {
            using var stream = new FileStream(
                before.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.RandomAccess);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            PEHeaders headers = reader.PEHeaders;
            if (headers.PEHeader is not null)
            {
                isPortableExecutable = true;
                hasMetadata = reader.HasMetadata;
                isManaged = headers.CorHeader is not null;
                machine = headers.CoffHeader.Machine.ToString();
                subsystem = headers.PEHeader.Subsystem.ToString();
                characteristics = headers.CoffHeader.Characteristics.ToString();

                if (headers.SectionHeaders.Length > maximumSections)
                {
                    throw new InvalidDataException(
                        $"The portable executable has {headers.SectionHeaders.Length} sections; the limit is {maximumSections}.");
                }

                foreach (SectionHeader section in headers.SectionHeaders)
                {
                    sections.Add(new PortableExecutableSection
                    {
                        Name = section.Name,
                        VirtualSize = section.VirtualSize,
                        VirtualAddress = section.VirtualAddress,
                        RawDataSize = section.SizeOfRawData,
                        RawDataPointer = section.PointerToRawData,
                        Characteristics = section.SectionCharacteristics.ToString(),
                    });
                }
            }
        }
        catch (BadImageFormatException)
        {
            isPortableExecutable = false;
            sections.Clear();
        }

        FileInfo after = GetRegularFile(before.FullName, maximumFileBytes);
        if (after.Length != expectedLength || after.LastWriteTimeUtc.Ticks != expectedWriteTicks)
        {
            throw new IOException("The file changed while portable-executable evidence was collected.");
        }

        return new PortableExecutableReport
        {
            Path = before.FullName,
            Sha256 = sha256,
            Length = expectedLength,
            IsPortableExecutable = isPortableExecutable,
            HasMetadata = hasMetadata,
            IsManaged = isManaged,
            Machine = machine,
            Subsystem = subsystem,
            Characteristics = characteristics,
            Entropy = entropy,
            Sections = sections,
        };
    }

    private static FileInfo GetRegularFile(string path, long maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        if (maximumBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        string fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        info.Refresh();
        if (!info.Exists)
        {
            throw new FileNotFoundException("The file does not exist.", fullPath);
        }

        if ((info.Attributes & FileAttributes.Directory) != 0 ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The path is not a regular non-reparse file.");
        }

        if (info.Length < 0 || info.Length > maximumBytes)
        {
            throw new IOException($"The file exceeds the {maximumBytes}-byte limit.");
        }

        return info;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException("The file ended before the expected sample was read.");
            }
            offset += read;
        }
    }

    private static (string Sha256, double Entropy) HashAndMeasureEntropy(
        Stream stream,
        long expectedLength,
        long maximumBytes)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[BufferSize];
        long[] histogram = new long[256];
        long observed = 0;

        try
        {
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                observed += read;
                if (observed > maximumBytes)
                {
                    throw new IOException("The file exceeded the configured byte limit while being analyzed.");
                }

                hash.AppendData(buffer, 0, read);
                for (int index = 0; index < read; index++)
                {
                    histogram[buffer[index]]++;
                }
            }

            if (observed != expectedLength)
            {
                throw new IOException("The file length changed while being analyzed.");
            }

            byte[] digest = hash.GetHashAndReset();
            try
            {
                return (Convert.ToHexString(digest).ToLowerInvariant(), CalculateEntropy(histogram, observed));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            Array.Clear(histogram, 0, histogram.Length);
        }
    }

    private static double CalculateEntropy(long[] histogram, long total)
    {
        if (total <= 0)
        {
            return 0;
        }

        double entropy = 0;
        foreach (long count in histogram)
        {
            if (count == 0)
            {
                continue;
            }

            double probability = (double)count / total;
            entropy -= probability * Math.Log2(probability);
        }

        return Math.Round(entropy, 6, MidpointRounding.AwayFromZero);
    }
}
