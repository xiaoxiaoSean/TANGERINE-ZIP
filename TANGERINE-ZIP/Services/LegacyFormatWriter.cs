using System.Buffers.Binary;
using System.Text;
using Compression.Core.Streams;
using FileFormat.Ace;
using FileFormat.Arc;
using FileFormat.Arj;
using FileFormat.Compress;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: LGWRT. The package's default ARC and Unix-compress modes do
// not interoperate with the application's reader. The modes below were
// independently checked with SharpCompress and, for ARJ/.Z, with 7-Zip.
internal static class LegacyFormatWriter
{
    public static async Task CreateAsync(IReadOnlyList<string> sources, string destination,
        FileDetector.FileType type, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (type == FileDetector.FileType.Lzw)
        {
            if (sources.Count != 1 || !File.Exists(sources[0]))
                throw new StageException("LGWRT0001", LanguageManager.Get("SingleInputRequired")); //LGWRT0001
            await CreateLzwAsync(sources[0], destination, progress, token);
            return;
        }
        if (type is not (FileDetector.FileType.Arj or FileDetector.FileType.Ace or FileDetector.FileType.Arc))
            throw new StageException("LGWRT0002", LanguageManager.Get("UnsupportedCreateFormat")); //LGWRT0002

        List<(string Path, string Key)> files = [];
        foreach (string source in sources)
        {
            token.ThrowIfCancellationRequested();
            string full = Path.GetFullPath(source);
            if (File.Exists(full)) files.Add((full, Path.GetFileName(full)));
            else if (Directory.Exists(full))
            {
                foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    files.Add((file, Path.Combine(Path.GetFileName(full), Path.GetRelativePath(full, file))
                        .Replace(Path.DirectorySeparatorChar, '/')));
            }
            else throw new StageException("LGWRT0003", LanguageManager.Get("LegacySourceMissing")); //LGWRT0003
        }
        if (files.Count == 0)
            throw new StageException("LGWRT0003", LanguageManager.Get("LegacySourceMissing")); //LGWRT0003
        if (files.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new StageException("LGWRT0004", LanguageManager.Get("LegacyDuplicateNames")); //LGWRT0004

        if (type == FileDetector.FileType.Ace)
        {
            if (files.Any(item => item.Key.Any(character => character > 127)))
                throw new StageException("LGWRT0006", LanguageManager.Get("AceNameUnsupported")); //LGWRT0006
            await CreateAceStoredAsync(files, destination, progress, token);
        }
        else if (type == FileDetector.FileType.Arc)
        {
            // Classic ARC has a flat 13-byte DOS member name. Reject paths
            // that would be truncated or collide instead of silently losing
            // directory structure or overwriting a member.
            if (files.Any(item => item.Key.Contains('/') || item.Key.Length > 12 ||
                item.Key.Any(character => character > 127)))
                throw new StageException("LGWRT0005", LanguageManager.Get("ArcNameUnsupported")); //LGWRT0005
            await CreateArcAsync(files, destination, progress, token);
        }
        else await CreateArjAsync(files, destination, progress, token);
    }

    private static async Task CreateArjAsync(IReadOnlyList<(string Path, string Key)> files,
        string destination, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        // Method 1 is a compressed ARJ method and its output was verified by
        // both independent readers. ArjWriter accepts each member in memory.
        ArjWriter writer = new(1, null);
        long totalBytes = TotalSourceBytes(files);
        long completedBytes = 0;
        for (int index = 0; index < files.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            (string path, string key) = files[index];
            byte[] contents = await ReadMemberAsync(path, key, progress, totalBytes,
                () => completedBytes, value => completedBytes += value, token);
            writer.AddFile(key.Replace('/', '\\'), contents, File.GetLastWriteTime(path));
            progress?.Report(new ArchiveProgress(90 + (index + 1) * 5 / files.Count, key));
        }
        token.ThrowIfCancellationRequested();
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        writer.WriteTo(output);
        await output.FlushAsync(token);
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static async Task CreateArcAsync(IReadOnlyList<(string Path, string Key)> files,
        string destination, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        // The package's Crunched mode emits a stream rejected by SharpCompress.
        // Packed mode is compressed and independently round-trips correctly.
        using ArcWriter writer = new(output, ArcCompressionMethod.Packed, leaveOpen: true);
        long totalBytes = TotalSourceBytes(files);
        long completedBytes = 0;
        for (int index = 0; index < files.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            (string path, string key) = files[index];
            byte[] contents = await ReadMemberAsync(path, key, progress, totalBytes,
                () => completedBytes, value => completedBytes += value, token);
            writer.AddEntry(key, contents, ArcCompressionMethod.Packed,
                new DateTimeOffset(File.GetLastWriteTime(path)));
            progress?.Report(new ArchiveProgress(90 + (index + 1) * 5 / files.Count, key));
        }
        writer.Finish();
        await output.FlushAsync(token);
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static async Task CreateLzwAsync(string source, string destination,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read,
            FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        // The package's block-reset mode produces unreadable .Z streams.
        // Non-block mode with a 16-bit dictionary is accepted by both readers.
        await using CompressStream encoder = new(output, CompressionStreamMode.Compress,
            maxBits: 16, blockMode: false, leaveOpen: true);
        await using ProgressStream monitored = new(input, input.Length, Path.GetFileName(source), progress);
        await monitored.CopyToAsync(encoder, token);
        await encoder.FlushAsync(token);
        progress?.Report(new ArchiveProgress(100, Path.GetFileName(source)));
    }

    private static async Task CreateAceStoredAsync(IReadOnlyList<(string Path, string Key)> files,
        string destination, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        // AceWriter's compressed payload and header encoding fail independent
        // ACE readers. Use its ACE 2 main-header template, then write validated
        // stored file records directly. Stored entries keep the archive fully
        // interoperable while avoiding a false claim of compressed ACE data.
        byte[] main = new AceWriter(15, null, false, false, 0, 0).ToArray();
        if (main.Length < 14 || main[7] != '*' || main[8] != '*' || main[9] != 'A')
            throw new StageException("LGWRT0007", LanguageManager.Get("LegacyWriterInvalid")); //LGWRT0007
        ushort mainSize = BinaryPrimitives.ReadUInt16LittleEndian(main.AsSpan(2, 2));
        if (main.Length != mainSize + 4)
            throw new StageException("LGWRT0007", LanguageManager.Get("LegacyWriterInvalid")); //LGWRT0007
        BinaryPrimitives.WriteUInt16LittleEndian(main.AsSpan(0, 2),
            (ushort)Crc32Checksum.Update(uint.MaxValue, main.AsSpan(4)));

        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        await output.WriteAsync(main, token);
        byte[] buffer = new byte[128 * 1024];
        long totalBytes = TotalSourceBytes(files);
        double completedBytes = 0;
        for (int index = 0; index < files.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            (string path, string key) = files[index];
            FileInfo source = new(path);
            long length = source.Length;
            DateTime written = source.LastWriteTimeUtc;
            byte[] name = Encoding.ASCII.GetBytes(key.Replace('/', '\\'));
            if (length > uint.MaxValue || name.Length > ushort.MaxValue - 31)
                throw new StageException("LGWRT0008", LanguageManager.Get("AceSizeUnsupported")); //LGWRT0008

            // ACE stores the bitwise-inverted conventional CRC-32. Read once
            // to calculate the header value, then stream the file into the
            // archive without retaining its contents in memory.
            uint aceCrc = uint.MaxValue;
            await using (FileStream input = new(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, buffer.Length, FileOptions.Asynchronous))
            {
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    aceCrc = Crc32Checksum.Update(aceCrc, buffer.AsSpan(0, read));
                    completedBytes += read;
                    progress?.Report(new ArchiveProgress((int)(completedBytes * 100 /
                        Math.Max(2.0 * totalBytes, 1)), key));
                }
            }
            source.Refresh();
            if (source.Length != length || source.LastWriteTimeUtc != written)
                throw new StageException("LGWRT0009", LanguageManager.Get("LegacySourceChanged")); //LGWRT0009

            byte[] body = new byte[31 + name.Length];
            body[0] = 1; // ACE FILE32 header.
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1, 2), 1); // Added data follows.
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(3, 4), (uint)length);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(7, 4), (uint)length);
            DateTime local = File.GetLastWriteTime(path);
            uint timestamp = (uint)((Math.Clamp(local.Year, 1980, 2107) - 1980) << 25 |
                local.Month << 21 | local.Day << 16 | local.Hour << 11 |
                local.Minute << 5 | local.Second / 2);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(11, 4), timestamp);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(19, 4), aceCrc);
            body[23] = 0; // Store method; no proprietary LZ77 payload.
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(25, 2), 5);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(29, 2), (ushort)name.Length);
            name.CopyTo(body.AsSpan(31));
            byte[] header = new byte[body.Length + 4];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2),
                (ushort)Crc32Checksum.Update(uint.MaxValue, body));
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2, 2), (ushort)body.Length);
            body.CopyTo(header.AsSpan(4));
            await output.WriteAsync(header, token);
            await using (FileStream input = new(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, buffer.Length, FileOptions.Asynchronous))
            {
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    completedBytes += read;
                    progress?.Report(new ArchiveProgress((int)(completedBytes * 100 /
                        Math.Max(2.0 * totalBytes, 1)), key));
                }
            }
            source.Refresh();
            if (source.Length != length || source.LastWriteTimeUtc != written)
                throw new StageException("LGWRT0009", LanguageManager.Get("LegacySourceChanged")); //LGWRT0009
            progress?.Report(new ArchiveProgress((index + 1) * 100 / files.Count, key));
        }
        await output.FlushAsync(token);
    }

    private static long TotalSourceBytes(IReadOnlyList<(string Path, string Key)> files)
    {
        // Saturating addition keeps progress arithmetic well defined for very
        // large source sets without changing the archive writer's own limits.
        long total = 0;
        foreach ((string path, _) in files)
        {
            long length = new FileInfo(path).Length;
            total = length > long.MaxValue - total ? long.MaxValue : total + length;
        }
        return total;
    }

    private static async Task<byte[]> ReadMemberAsync(string path, string key,
        IProgress<ArchiveProgress>? progress, long totalBytes, Func<long> completed,
        Action<long> advance, CancellationToken token)
    {
        // These two NuGet writers require a whole member as byte[]. Report
        // progress during the asynchronous read, before their synchronous
        // compression step, and reject members that cannot fit in an array.
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
        if (input.Length > int.MaxValue)
            throw new StageException("LGWRT0010", LanguageManager.Get("LegacyMemberTooLarge")); //LGWRT0010
        using MemoryStream contents = new(input.Length <= int.MaxValue ? (int)input.Length : 0);
        byte[] buffer = new byte[128 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            await contents.WriteAsync(buffer.AsMemory(0, read), token);
            advance(read);
            progress?.Report(new ArchiveProgress((int)(Math.Min(completed() * 90.0 /
                Math.Max(totalBytes, 1), 90)), key));
        }
        return contents.ToArray();
    }
}
