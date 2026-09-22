using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: PWDAR
// TZIP password envelopes provide authenticated encryption for archive formats that do not define encryption themselves.
internal static class PasswordArchiveService
{
    private static readonly byte[] Magic = "TZIPENC2"u8.ToArray();
    private const byte Version = 1;
    private const int SaltLength = 16;
    private const int NoncePrefixLength = 8;
    private const int TagLength = 16;
    private const int KeyLength = 32;
    private const int ChunkSize = 1024 * 1024;
    private const int Iterations = 600_000;
    private const int HeaderLength = 8 + 1 + 4 + 4 + SaltLength + NoncePrefixLength + 4 + 8 + TagLength;

    public static bool IsProtected(string path) => TryReadProtectedType(path, out _);

    public static bool TryReadProtectedType(string path, out FileDetector.FileType type)
    {
        type = FileDetector.FileType.Unknown;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return TryReadProtectedType(stream, out type);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static bool TryReadProtectedType(Stream stream, out FileDetector.FileType type)
    {
        type = FileDetector.FileType.Unknown;
        if (!stream.CanSeek || stream.Length - stream.Position < HeaderLength) return false;
        long position = stream.Position;
        Span<byte> prefix = stackalloc byte[13];
        try
        {
            if (stream.Read(prefix) != prefix.Length || !prefix[..8].SequenceEqual(Magic) || prefix[8] != Version) return false;
            int rawType = BinaryPrimitives.ReadInt32LittleEndian(prefix[9..13]);
            if (!Enum.IsDefined(typeof(FileDetector.FileType), rawType)) return false;
            type = (FileDetector.FileType)rawType;
            return ArchiveCapabilities.CanOpen(type);
        }
        finally { stream.Position = position; }
    }

    public static async Task EncryptAsync(string sourcePath, string outputPath, FileDetector.FileType type,
        string password, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrEmpty(password)) throw new StageException("PWDAR0001", LanguageManager.Get("ArchivePasswordRequired")); //PWDAR0001
        string temporaryOutput = outputPath + "." + Guid.NewGuid().ToString("N") + ".tzpw";
        try
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
            byte[] noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixLength);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
            try
            {
                await using FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
                await using FileStream output = new(temporaryOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None, ChunkSize, true);
                byte[] headerWithoutVerifier = BuildHeader(type, salt, noncePrefix, input.Length);
                byte[] verifier = new byte[TagLength];
                using AesGcm cipher = new(key, TagLength);
                cipher.Encrypt(BuildNonce(noncePrefix, uint.MaxValue), ReadOnlySpan<byte>.Empty, Span<byte>.Empty, verifier, headerWithoutVerifier);
                await output.WriteAsync(headerWithoutVerifier, token);
                await output.WriteAsync(verifier, token);

                byte[] plain = new byte[ChunkSize];
                byte[] encrypted = new byte[ChunkSize];
                byte[] tag = new byte[TagLength];
                long completed = 0;
                uint chunkIndex = 0;
                int count;
                while ((count = await input.ReadAsync(plain, token)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    byte[] associatedData = BuildChunkAssociatedData(headerWithoutVerifier, chunkIndex, count);
                    cipher.Encrypt(BuildNonce(noncePrefix, chunkIndex), plain.AsSpan(0, count), encrypted.AsSpan(0, count), tag, associatedData);
                    byte[] length = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(length, count);
                    await output.WriteAsync(length, token);
                    await output.WriteAsync(encrypted.AsMemory(0, count), token);
                    await output.WriteAsync(tag, token);
                    completed += count;
                    progress?.Report(new ArchiveProgress((int)(completed * 100 / Math.Max(input.Length, 1)), Path.GetFileName(sourcePath)));
                    checked { chunkIndex++; }
                }
                await output.FlushAsync(token);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            File.Move(temporaryOutput, outputPath, true);
            progress?.Report(new ArchiveProgress(100, string.Empty));
        }
        catch (StageException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw new StageException("PWDAR0005", LanguageManager.Get("ArchivePasswordCreateFailed") + Environment.NewLine + exception.Message, exception); } //PWDAR0005
        finally
        {
            try { if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput); }
            catch (Exception exception) { throw new StageException("PWDAR0004", LanguageManager.Get("ArchivePasswordCleanupFailed"), exception); } //PWDAR0004
        }
    }

    public static async Task<MaterializedArchive> OpenAsync(string path, string? password,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (!TryReadProtectedType(path, out FileDetector.FileType type)) return MaterializedArchive.Unprotected(path);
        if (string.IsNullOrEmpty(password)) throw new StageException("PWDAR0001", LanguageManager.Get("ArchivePasswordRequired")); //PWDAR0001

        string directory = Path.Combine(Path.GetTempPath(), "TangerineZipPassword", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string materializedPath = Path.Combine(directory, Path.GetFileName(path));
        FileStream? output = null;
        try
        {
            await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
            byte[] header = new byte[HeaderLength];
            await ReadExactlyAsync(input, header, token);
            ValidateHeader(header, type, out int iterations, out byte[] salt, out byte[] noncePrefix, out long originalLength);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeyLength);
            try
            {
                using AesGcm cipher = new(key, TagLength);
                try
                {
                    cipher.Decrypt(BuildNonce(noncePrefix, uint.MaxValue), ReadOnlySpan<byte>.Empty, header.AsSpan(HeaderLength - TagLength), Span<byte>.Empty, header.AsSpan(0, HeaderLength - TagLength));
                }
                catch (AuthenticationTagMismatchException exception)
                {
                    throw new StageException("PWDAR0002", LanguageManager.Get("ArchivePasswordInvalid"), exception); //PWDAR0002
                }

                // DeleteOnClose also removes plaintext when the worker is force-killed and its handles are closed by Windows.
                output = new FileStream(materializedPath, FileMode.CreateNew, FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete, ChunkSize, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                byte[] encrypted = new byte[ChunkSize];
                byte[] plain = new byte[ChunkSize];
                byte[] tag = new byte[TagLength];
                long completed = 0;
                uint chunkIndex = 0;
                while (completed < originalLength)
                {
                    byte[] lengthBytes = new byte[4];
                    await ReadExactlyAsync(input, lengthBytes, token);
                    int count = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                    if (count <= 0 || count > ChunkSize || count > originalLength - completed)
                        throw new StageException("PWDAR0003", LanguageManager.Get("ArchivePasswordDamaged")); //PWDAR0003
                    await ReadExactlyAsync(input, encrypted.AsMemory(0, count), token);
                    await ReadExactlyAsync(input, tag, token);
                    try
                    {
                        cipher.Decrypt(BuildNonce(noncePrefix, chunkIndex), encrypted.AsSpan(0, count), tag,
                            plain.AsSpan(0, count), BuildChunkAssociatedData(header.AsSpan(0, HeaderLength - TagLength), chunkIndex, count));
                    }
                    catch (AuthenticationTagMismatchException exception)
                    {
                        throw new StageException("PWDAR0003", LanguageManager.Get("ArchivePasswordDamaged"), exception); //PWDAR0003
                    }
                    await output.WriteAsync(plain.AsMemory(0, count), token);
                    completed += count;
                    progress?.Report(new ArchiveProgress((int)(completed * 100 / Math.Max(originalLength, 1)), Path.GetFileName(path)));
                    checked { chunkIndex++; }
                }
                if (input.Position != input.Length) throw new StageException("PWDAR0003", LanguageManager.Get("ArchivePasswordDamaged")); //PWDAR0003
                await output.FlushAsync(token);
                output.Position = 0;
                return new MaterializedArchive(materializedPath, output, directory);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (Exception primaryException)
        {
            if (output is not null) await output.DisposeAsync();
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception cleanupException)
            {
                throw new StageException("PWDAR0004", LanguageManager.Get("ArchivePasswordCleanupFailed"),
                    new AggregateException(primaryException, cleanupException)); //PWDAR0004
            }
            throw;
        }
    }

    private static byte[] BuildHeader(FileDetector.FileType type, byte[] salt, byte[] noncePrefix, long originalLength)
    {
        byte[] header = new byte[HeaderLength - TagLength];
        Magic.CopyTo(header, 0);
        header[8] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), (int)type);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(13, 4), Iterations);
        salt.CopyTo(header, 17);
        noncePrefix.CopyTo(header, 33);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(41, 4), ChunkSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(45, 8), originalLength);
        return header;
    }

    private static void ValidateHeader(byte[] header, FileDetector.FileType expectedType, out int iterations,
        out byte[] salt, out byte[] noncePrefix, out long originalLength)
    {
        if (!header.AsSpan(0, 8).SequenceEqual(Magic) || header[8] != Version ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(9, 4)) != (int)expectedType ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(41, 4)) != ChunkSize)
            throw new StageException("PWDAR0003", LanguageManager.Get("ArchivePasswordDamaged")); //PWDAR0003
        iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(13, 4));
        originalLength = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(45, 8));
        if (iterations < 100_000 || iterations > 5_000_000 || originalLength < 0)
            throw new StageException("PWDAR0003", LanguageManager.Get("ArchivePasswordDamaged")); //PWDAR0003
        salt = header.AsSpan(17, SaltLength).ToArray();
        noncePrefix = header.AsSpan(33, NoncePrefixLength).ToArray();
    }

    private static byte[] BuildNonce(byte[] prefix, uint chunkIndex)
    {
        byte[] nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), chunkIndex);
        return nonce;
    }

    private static byte[] BuildChunkAssociatedData(ReadOnlySpan<byte> header, uint chunkIndex, int count)
    {
        byte[] data = new byte[header.Length + 8];
        header.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(header.Length), chunkIndex);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(header.Length + 4), count);
        return data;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        try { await stream.ReadExactlyAsync(buffer, token); }
        catch (EndOfStreamException exception) { throw new StageException("PWDAR0003", LanguageManager.Get("ArchivePasswordDamaged"), exception); } //PWDAR0003
    }
}

internal sealed class MaterializedArchive : IAsyncDisposable
{
    private readonly FileStream? _deleteOnCloseStream;
    private readonly string? _directory;
    private MaterializedArchive(string path) => Path = path;
    internal MaterializedArchive(string path, FileStream stream, string directory) { Path = path; _deleteOnCloseStream = stream; _directory = directory; }
    public string Path { get; }
    public static MaterializedArchive Unprotected(string path) => new(path);

    public async ValueTask DisposeAsync()
    {
        if (_deleteOnCloseStream is null) return;
        try
        {
            await _deleteOnCloseStream.DisposeAsync();
            if (_directory is not null && Directory.Exists(_directory)) Directory.Delete(_directory);
        }
        catch (Exception exception) { throw new StageException("PWDAR0004", LanguageManager.Get("ArchivePasswordCleanupFailed"), exception); } //PWDAR0004
    }
}
