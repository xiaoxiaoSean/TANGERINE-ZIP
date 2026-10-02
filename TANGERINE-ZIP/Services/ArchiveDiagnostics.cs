using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DiscUtils.Iso9660;
using SharpCompress.Archives;
using SharpCompress.Common.Options;
using SharpCompress.Readers;
using SharpCompress.Compressors.Lzw;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

internal enum IntegrityMode { Crc, Hash, DataBlocks }
internal sealed record EntryTestResult(string Name, bool Healthy, string Message);

internal static class ArchiveDiagnostics
{
    public static async Task<string> ComputeHashAsync(string path, string algorithm, CancellationToken token)
    {
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using HashAlgorithm hash = algorithm.ToUpperInvariant() switch
        {
            "SHA256" => SHA256.Create(), "SHA512" => SHA512.Create(), "MD5" => MD5.Create(),
            _ => throw new ArgumentException(nameof(algorithm))
        };
        return Convert.ToHexString(await hash.ComputeHashAsync(input, token));
    }

    public static async Task<string> TestAsync(string path, IntegrityMode mode, string? expectedHash,
        string? password, CancellationToken token)
    {
        if (mode == IntegrityMode.Hash)
        {
            string actual = await ComputeHashAsync(path, "SHA256", token);
            return string.IsNullOrWhiteSpace(expectedHash)
                ? string.Format(LanguageManager.Get("IntegrityHashResult"), actual)
                : string.Format(LanguageManager.Get(actual.Equals(expectedHash.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? "IntegrityHashMatch" : "IntegrityHashMismatch"), actual);
        }
        if (mode == IntegrityMode.DataBlocks)
        {
            await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[1024 * 1024];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, token)) != 0) read += count;
            return string.Format(LanguageManager.Get("IntegrityBlocksResult"), ResourcePreflight.FormatBytes(read));
        }
        IReadOnlyList<EntryTestResult> entries = await ScanEntriesAsync(path, password, token);
        int bad = entries.Count(e => !e.Healthy);
        return string.Format(LanguageManager.Get("IntegrityCrcResult"), entries.Count, bad) +
            Environment.NewLine + string.Join(Environment.NewLine, entries.Where(e => !e.Healthy)
                .Select(e => e.Name + ": " + e.Message));
    }

    public static Task<IReadOnlyList<EntryTestResult>> ScanEntriesAsync(string path, string? password, CancellationToken token) =>
        Task.Run<IReadOnlyList<EntryTestResult>>(() =>
        {
            List<EntryTestResult> results = [];
            bool forwardOnly = FileDetector.DetectFileType(path) is
                FileDetector.FileType.Arj or FileDetector.FileType.Ace or
                FileDetector.FileType.Arc;
            FileDetector.FileType type = FileDetector.DetectFileType(path);
            if (type == FileDetector.FileType.Iso)
            {
                // An ISO has no per-file CRC field. Reading every file through
                // DiscUtils still checks directory records and data extents.
                // Report each readable file instead of a misleading zero count.
                using FileStream image = File.OpenRead(path);
                using CDReader iso = new(image, true);
                foreach (string key in iso.GetFiles("", "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();
                    CheckEntry(key, 0, () => iso.OpenFile(key, FileMode.Open));
                }
            }
            else if (type is FileDetector.FileType.Lzw or FileDetector.FileType.Lzip)
            {
                // Standalone LZW and LZip have no archive directory. Read the
                // complete decoded stream so checksum/decoder failures surface.
                string key = Path.GetFileNameWithoutExtension(path);
                CheckEntry(key, 0, () =>
                {
                    FileStream input = File.OpenRead(path);
                    try
                    {
                        return type == FileDetector.FileType.Lzw
                            ? new LzwStream(input)
                            : SharpCompress.Compressors.LZMA.LZipStream.Create(input,
                                SharpCompress.Compressors.CompressionMode.Decompress, leaveOpen: false);
                    }
                    catch { input.Dispose(); throw; }
                });
            }
            else if (forwardOnly)
            {
                // The Reader API is the only SharpCompress path for these
                // legacy formats. Drain every member to verify available CRCs.
                using IReader reader = ReaderFactory.OpenReader(path, new ReaderOptions { Password = password });
                while (reader.MoveToNextEntry())
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.Entry.IsDirectory) continue;
                    CheckEntry(reader.Entry.Key ?? string.Empty, reader.Entry.Crc, reader.OpenEntryStream);
                }
            }
            else
            {
                using IArchive archive = ArchiveFactory.OpenArchive(path, new ReaderOptions { Password = password });
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    token.ThrowIfCancellationRequested();
                    CheckEntry(entry.Key ?? string.Empty, entry.Crc, entry.OpenEntryStream);
                }
            }
            return results;

            void CheckEntry(string key, long expectedCrc, Func<Stream> open)
            {
                try
                {
                    using Stream input = open();
                    byte[] buffer = new byte[128 * 1024];
                    uint crc = uint.MaxValue;
                    ushort arcCrc = 0;
                    int read;
                    while ((read = input.Read(buffer)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        crc = Crc32Checksum.Update(crc, buffer.AsSpan(0, read));
                        if (type == FileDetector.FileType.Arc)
                        {
                            // Classic ARC stores CRC-16/IBM (seed zero) rather
                            // than a CRC-32. Its reader exposes that 16-bit
                            // value through the common Entry.Crc property.
                            foreach (byte value in buffer.AsSpan(0, read))
                            {
                                arcCrc ^= value;
                                for (int bit = 0; bit < 8; bit++)
                                    arcCrc = (ushort)((arcCrc >> 1) ^
                                        ((arcCrc & 1) != 0 ? 0xA001 : 0));
                            }
                        }
                    }
                    if (type == FileDetector.FileType.Arc
                        ? arcCrc != (ushort)expectedCrc
                        : type == FileDetector.FileType.Ace
                            ? expectedCrc > 0 && expectedCrc <= uint.MaxValue && (uint)expectedCrc != crc
                            : !Crc32Checksum.Matches(expectedCrc, crc))
                        throw new InvalidDataException(LanguageManager.Get("IntegrityCrcMismatch"));
                    results.Add(new(key, true, LanguageManager.Get("IntegrityHealthy")));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    // Decoder exception text comes from external libraries and can
                    // ignore the selected UI language. Keep a stable translated
                    // result while retaining the detailed exception in the caller's
                    // diagnostic boundary if archive opening itself fails.
                    string message = error is InvalidDataException &&
                        error.Message == LanguageManager.Get("IntegrityCrcMismatch")
                            ? error.Message : LanguageManager.Get("IntegrityReadFailed");
                    results.Add(new(key, false, message));
                }
            }
        }, token);

    public static Task<string> RecoverAsync(string path, string? password, CancellationToken token) => Task.Run(() =>
    {
        if (FileDetector.DetectFileType(path) is not
            (FileDetector.FileType.Zip or FileDetector.FileType.Rar or
             FileDetector.FileType.SevenZip or FileDetector.FileType.Tar or FileDetector.FileType.GZip))
            throw new StageException("ARDIA0001", LanguageManager.Get("RepairFormatUnsupported")); //ARDIA0001
        string backup = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "." + Guid.NewGuid().ToString("N") + ".bak";
        string recovered = path + ".recovered." + DateTime.Now.ToString("yyyyMMddHHmmss") + "." +
            Guid.NewGuid().ToString("N") + ".zip";
        string temporary = recovered + "." + Guid.NewGuid().ToString("N") + ".tmp";
        long sourceSize = new FileInfo(path).Length;
        ResourcePreflight.Check(recovered, sourceSize > (long.MaxValue - 64L * 1024 * 1024) / 2
            ? long.MaxValue : sourceSize * 2 + 64L * 1024 * 1024, 128L * 1024 * 1024);
        // The original is copied before any recovery output is written. Recovery never edits it.
        File.Copy(path, backup, overwrite: false);
        int good = 0, bad = 0;
        try
        {
            using IArchive source = ArchiveFactory.OpenArchive(path, new ReaderOptions { Password = password });
            using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive target = new(file, ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries.Where(e => !e.IsDirectory))
                {
                    token.ThrowIfCancellationRequested();
                    string key = (entry.Key ?? string.Empty).Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') ||
                        key.Split('/').Any(segment => segment is ".." or "") || key.Contains(':')) { bad++; continue; }
                    long compressedBaseline = entry.CompressedSize > 0 ? entry.CompressedSize : sourceSize;
                    if (entry.Size >= 1024L * 1024 * 1024 && compressedBaseline > 0 &&
                        entry.Size / compressedBaseline >= 1000) { bad++; continue; }
                    // Stage one entry before adding it. A corrupt stream must not leave a partial
                    // member in the recovered archive.
                    string staged = Path.Combine(TempDirectorySettings.GetDirectory(
                        Math.Max(entry.Size, 0) > long.MaxValue - 64L * 1024 * 1024 ? long.MaxValue :
                            Math.Max(entry.Size, 0) + 64L * 1024 * 1024),
                        "TZRecovery_" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        using (Stream input = entry.OpenEntryStream())
                        using (FileStream output = new(staged, FileMode.CreateNew, FileAccess.Write))
                        {
                            byte[] buffer = new byte[128 * 1024];
                            long written = 0, lastCheck = 0;
                            uint crc = uint.MaxValue;
                            int read;
                            while ((read = input.Read(buffer)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                written += read;
                                if (written >= 1024L * 1024 * 1024 && compressedBaseline > 0 &&
                                    written / compressedBaseline >= 1000)
                                    throw new InvalidDataException(LanguageManager.Get("BombPrompt"));
                                if (written - lastCheck >= 64L * 1024 * 1024)
                                {
                                    ResourcePreflight.Check(staged, 16L * 1024 * 1024, 128L * 1024 * 1024);
                                    lastCheck = written;
                                }
                                output.Write(buffer, 0, read);
                                crc = Crc32Checksum.Update(crc, buffer.AsSpan(0, read));
                            }
                            if (!Crc32Checksum.Matches(entry.Crc, crc))
                                throw new InvalidDataException(LanguageManager.Get("IntegrityCrcMismatch"));
                        }
                        ZipArchiveEntry newEntry = target.CreateEntry(key, CompressionLevel.Optimal);
                        using Stream recoveredOutput = newEntry.Open();
                        using FileStream stagedInput = File.OpenRead(staged);
                        stagedInput.CopyTo(recoveredOutput);
                        good++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { bad++; }
                    finally { if (File.Exists(staged)) File.Delete(staged); }
                }
            }
            File.Move(temporary, recovered);
            return string.Format(LanguageManager.Get("RepairResult"), good, bad, backup, recovered);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);
}
