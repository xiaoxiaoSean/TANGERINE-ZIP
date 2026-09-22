using DiscUtils.Iso9660;
using Joveler.Compression.XZ;
using K4os.Compression.LZ4.Streams;
using ManagedWimLib;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Common.Options;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.ZStandard;
using SharpCompress.Readers;
using SharpCompress.Writers;
using System.IO.Compression;
using System.Text;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

internal sealed class ArchiveService
{
    private static readonly object NativeInitializationLock = new();
    private static bool _xzInitialized;
    private static bool _wimInitialized;
    private static readonly ArchiveEncoding UnicodeArchiveEncoding = new()
    {
        // SharpCompress compares this encoding with Encoding.UTF8 to emit ZIP's EFS flag.
        // UTF8Encoding(false) writes UTF-8 bytes but compares unequal, so other tools use legacy code pages.
        // GetBytes never writes the preamble, so Encoding.UTF8 does not add a BOM to entry names.
        Default = Encoding.UTF8,
        UTF8 = Encoding.UTF8
    };

    private static ReaderOptions CreateReaderOptions(string? password = null) => new()
    {
        ArchiveEncoding = UnicodeArchiveEncoding,
        Password = password
    };

    public Task<IReadOnlyList<ArchiveEntryInfo>> ListAsync(string archivePath, CancellationToken cancellationToken, string? password = null)
    {
        return Task.Run<IReadOnlyList<ArchiveEntryInfo>>(() =>
        {
            try
            {
                FileDetector.FileType type = FileDetector.DetectFileType(archivePath);
                return type switch
                {
                    FileDetector.FileType.Iso => ListIso(archivePath),
                    FileDetector.FileType.Wim => ListWim(archivePath),
                    FileDetector.FileType.GZip or FileDetector.FileType.BZip2 or FileDetector.FileType.Lz4 or FileDetector.FileType.Xz or FileDetector.FileType.Zstd =>
                        [new ArchiveEntryInfo(GetRawOutputName(archivePath), false, new FileInfo(archivePath).Length)],
                    _ => ListSharpCompress(archivePath, cancellationToken, password)
                };
            }
            catch (StageException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsPasswordFailure(exception))
            {
                throw CreatePasswordException(password, exception);
            }
            catch (Exception exception)
            {
                throw new StageException("ARCSV0001", exception.Message, exception); //ARCSV0001
            }
        }, cancellationToken);
    }

    public Task<NestedTarInfo> AnalyzeNestedTarAsync(string archivePath, CancellationToken cancellationToken,
        string? password = null, IProgress<ArchiveProgress>? progress = null)
    {
        return Task.Run(() =>
        {
            try
            {
                FileDetector.FileType outerType = FileDetector.DetectFileType(archivePath);
                if (ArchiveCapabilities.IsSingleFileStream(outerType))
                {
                    using FileStream input = File.OpenRead(archivePath);
                    using Stream decoder = CreateDecoder(input, outerType);
                    return FileDetector.DetectFileType(decoder) == FileDetector.FileType.Tar
                        ? new NestedTarInfo([GetRawOutputName(archivePath)], true)
                        : NestedTarInfo.None;
                }

                if (outerType is not (FileDetector.FileType.Zip or FileDetector.FileType.Rar or FileDetector.FileType.SevenZip))
                {
                    return NestedTarInfo.None;
                }

                using IArchive archive = ArchiveFactory.OpenArchive(archivePath, CreateReaderOptions(password));
                List<IArchiveEntry> fileEntries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
                if (fileEntries.Any(entry => entry.IsEncrypted) && string.IsNullOrEmpty(password))
                    throw new StageException("PWDAR0001", LanguageManager.Get("ArchivePasswordRequired")); //PWDAR0001
                List<string> tarEntries = [];
                long authenticationTotal = Math.Max(fileEntries.Where(entry => entry.IsEncrypted).Sum(entry => Math.Max(entry.Size, 1)), 1);
                long authenticated = 0;
                foreach (IArchiveEntry entry in fileEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using Stream stream = entry.OpenEntryStream();
                    FileDetector.FileType entryType = FileDetector.DetectFileType(stream);
                    // Some encryption formats authenticate only at the end of the entry. Drain encrypted
                    // entries during opening so an incorrect password is rejected before the UI stores it.
                    if (entry.IsEncrypted)
                        DrainForAuthentication(stream, entry.Key ?? string.Empty, authenticationTotal, ref authenticated, progress, cancellationToken);
                    if (entryType == FileDetector.FileType.Tar)
                    {
                        tarEntries.Add(NormalizeEntry(entry.Key ?? string.Empty));
                    }
                }
                return new NestedTarInfo(tarEntries, fileEntries.Count == 1 && tarEntries.Count == 1);
            }
            catch (StageException) { throw; }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsPasswordFailure(exception))
            {
                throw CreatePasswordException(password, exception);
            }
            catch (Exception exception)
            {
                throw new StageException("NESTR0001", exception.Message, exception); //NESTR0001
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveEntryInfo>> ListNestedTarAsync(string archivePath, string tarEntryKey, CancellationToken cancellationToken, string? password = null)
    {
        string temporaryTarPath = CreateTemporaryPath("nested.tar");
        try
        {
            await MaterializeNestedTarAsync(archivePath, tarEntryKey, temporaryTarPath, cancellationToken, password);
            return await ListAsync(temporaryTarPath, cancellationToken);
        }
        catch (StageException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StageException("NESTR0002", exception.Message, exception); //NESTR0002
        }
        finally
        {
            DeleteTemporaryFile(temporaryTarPath);
        }
    }

    public async Task ExtractNestedTarsAsync(
        string archivePath,
        IReadOnlyList<string> tarEntryKeys,
        string destinationPath,
        IReadOnlyCollection<string>? selectedInnerEntries,
        bool createTarSubfolders,
        OverwritePolicy overwritePolicy,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken,
        string? password = null)
    {
        try
        {
            for (int index = 0; index < tarEntryKeys.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string temporaryTarPath = CreateTemporaryPath("nested.tar");
                try
                {
                    await MaterializeNestedTarAsync(archivePath, tarEntryKeys[index], temporaryTarPath, cancellationToken, password);
                    string targetPath = createTarSubfolders
                        ? Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(Path.GetFileName(tarEntryKeys[index])))
                        : destinationPath;
                    InlineProgress<ArchiveProgress> nestedProgress = new(item =>
                    {
                        int totalPercent = ((index * 100) + item.Percentage) / Math.Max(tarEntryKeys.Count, 1);
                        progress?.Report(new ArchiveProgress(totalPercent, item.EntryKey));
                    });
                    await ExtractSharpCompressAsync(temporaryTarPath, targetPath, selectedInnerEntries, overwritePolicy, nestedProgress, cancellationToken);
                }
                finally
                {
                    DeleteTemporaryFile(temporaryTarPath);
                }
            }
            progress?.Report(new ArchiveProgress(100, string.Empty));
        }
        catch (StageException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StageException("NESTR0003", exception.Message, exception); //NESTR0003
        }
    }

    public async Task ExtractAsync(
        string archivePath,
        string destinationPath,
        IReadOnlyCollection<string>? selectedEntries,
        OverwritePolicy overwritePolicy,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken,
        string? password = null)
    {
        try
        {
            Directory.CreateDirectory(destinationPath);
            FileDetector.FileType type = FileDetector.DetectFileType(archivePath);
            switch (type)
            {
                case FileDetector.FileType.Iso:
                    await Task.Run(() => ExtractIso(archivePath, destinationPath, selectedEntries, overwritePolicy, progress, cancellationToken), cancellationToken);
                    break;
                case FileDetector.FileType.Wim:
                    await Task.Run(() => ExtractWim(archivePath, destinationPath, selectedEntries, overwritePolicy, progress), cancellationToken);
                    break;
                case FileDetector.FileType.GZip:
                case FileDetector.FileType.BZip2:
                case FileDetector.FileType.Lz4:
                case FileDetector.FileType.Xz:
                case FileDetector.FileType.Zstd:
                    await ExtractRawAsync(archivePath, destinationPath, type, overwritePolicy, progress, cancellationToken);
                    break;
                default:
                    await ExtractSharpCompressAsync(archivePath, destinationPath, selectedEntries, overwritePolicy, progress, cancellationToken, password);
                    break;
            }
        }
        catch (StageException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StageException("ARCSV0002", exception.Message, exception); //ARCSV0002
        }
    }

    public async Task CreateAsync(
        IReadOnlyList<string> sourcePaths,
        string outputPath,
        FileDetector.FileType type,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (type == FileDetector.FileType.Rar)
            {
                throw new StageException("ARCSV0003", LanguageManager.Get("RarCreateUnsupported")); //ARCSV0003
            }

            if (ArchiveCapabilities.IsSingleFileStream(type) && sourcePaths.Count != 1)
            {
                throw new StageException("ARCSV0004", LanguageManager.Get("SingleInputRequired")); //ARCSV0004
            }

            if (sourcePaths.Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)))
                throw new StageException("ARCSV0009", LanguageManager.Get("OutputConflictsInput")); //ARCSV0009

            string? parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            string temporaryOutputPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                switch (type)
                {
                case FileDetector.FileType.Iso:
                    await Task.Run(() => CreateIso(sourcePaths, temporaryOutputPath, progress, cancellationToken), cancellationToken);
                    break;
                case FileDetector.FileType.Wim:
                    await Task.Run(() => CreateWim(sourcePaths, temporaryOutputPath, progress), cancellationToken);
                    break;
                case FileDetector.FileType.GZip:
                case FileDetector.FileType.BZip2:
                case FileDetector.FileType.Xz:
                case FileDetector.FileType.Lz4:
                case FileDetector.FileType.Zstd:
                    await CreateRawAsync(sourcePaths[0], temporaryOutputPath, type, progress, cancellationToken);
                    break;
                case FileDetector.FileType.Zip:
                    await CreateZipAsync(sourcePaths, temporaryOutputPath, progress, cancellationToken);
                    break;
                case FileDetector.FileType.Tar:
                    await CreateTarAsync(sourcePaths, temporaryOutputPath, progress, cancellationToken);
                    break;
                case FileDetector.FileType.SevenZip:
                    await CreateSharpCompressAsync(sourcePaths, temporaryOutputPath, type, progress, cancellationToken);
                    break;
                default:
                    throw new StageException("ARCSV0005", LanguageManager.Get("UnsupportedFormat")); //ARCSV0005
                }
                File.Move(temporaryOutputPath, outputPath, true);
            }
            finally
            {
                if (File.Exists(temporaryOutputPath)) File.Delete(temporaryOutputPath);
            }
        }
        catch (StageException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StageException("ARCSV0006", exception.Message, exception); //ARCSV0006
        }
    }

    private static IReadOnlyList<ArchiveEntryInfo> ListSharpCompress(string archivePath, CancellationToken cancellationToken, string? password)
    {
        using IArchive archive = ArchiveFactory.OpenArchive(archivePath, CreateReaderOptions(password));
        if (archive.Entries.Any(entry => entry.IsEncrypted) && string.IsNullOrEmpty(password))
            throw new StageException("PWDAR0001", LanguageManager.Get("ArchivePasswordRequired")); //PWDAR0001
        return archive.Entries.Select(entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = NormalizeEntry(entry.Key ?? string.Empty);
            if (string.IsNullOrEmpty(key)) key = GetRawOutputName(archivePath);
            return new ArchiveEntryInfo(key, entry.IsDirectory, entry.Size);
        }).ToArray();
    }

    private static IReadOnlyList<ArchiveEntryInfo> ListIso(string archivePath)
    {
        using FileStream stream = File.OpenRead(archivePath);
        using CDReader reader = new(stream, true);
        List<ArchiveEntryInfo> result = reader.GetDirectories("", "*", SearchOption.AllDirectories)
            .Select(path => new ArchiveEntryInfo(NormalizeEntry(path) + "/", true, 0)).ToList();
        result.AddRange(reader.GetFiles("", "*", SearchOption.AllDirectories)
            .Select(path => new ArchiveEntryInfo(NormalizeEntry(path), false, reader.GetFileLength(path))));
        return result;
    }

    private static IReadOnlyList<ArchiveEntryInfo> ListWim(string archivePath)
    {
        EnsureWimInitialized();
        using Wim wim = Wim.OpenWim(archivePath, OpenFlags.None);
        List<ArchiveEntryInfo> result = [];
        WimInfo info = wim.GetWimInfo();
        for (int image = 1; image <= info.ImageCount; image++)
        {
            string imageRoot = $"{LanguageManager.Get("WimImage")} {image} - {wim.GetImageName(image)}/";
            result.Add(new ArchiveEntryInfo(imageRoot, true, 0));
            wim.IterateDirTree(image, "\\", IterateDirTreeFlags.Recursive, (entry, _) =>
            {
                string path = imageRoot + NormalizeEntry((entry.FullPath ?? string.Empty).TrimStart('\\'));
                bool isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
                result.Add(new ArchiveEntryInfo(isDirectory ? path.TrimEnd('/') + "/" : path, isDirectory, 0));
                return 0;
            });
        }
        return result;
    }

    private static async Task ExtractSharpCompressAsync(string archivePath, string destinationPath, IReadOnlyCollection<string>? selectedEntries, OverwritePolicy overwritePolicy, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken, string? password = null)
    {
        await Task.Run(() =>
        {
            using IArchive archive = ArchiveFactory.OpenArchive(archivePath, CreateReaderOptions(password));
            IArchiveEntry[] entries = archive.Entries.Where(entry => ShouldInclude(entry.Key ?? string.Empty, selectedEntries)).ToArray();
            if (entries.Any(entry => entry.IsEncrypted) && string.IsNullOrEmpty(password))
                throw new StageException("PWDAR0001", LanguageManager.Get("ArchivePasswordRequired")); //PWDAR0001
            long total = Math.Max(entries.Where(entry => !entry.IsDirectory).Sum(entry => Math.Max(entry.Size, 1)), 1);
            long completed = 0;
            foreach (IArchiveEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string key = NormalizeEntry(entry.Key ?? string.Empty);
                if (string.IsNullOrEmpty(key)) key = GetRawOutputName(archivePath);
                string targetPath = GetSafeTargetPath(destinationPath, key);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (File.Exists(targetPath) && overwritePolicy == OverwritePolicy.SkipAll)
                {
                    completed += Math.Max(entry.Size, 1);
                    continue;
                }
                string temporaryTarget = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using Stream input = entry.OpenEntryStream();
                    using (FileStream output = new(temporaryTarget, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        CopyWithProgress(input, output, entry.Size, () => completed, value => completed += value, total, key, progress, cancellationToken);
                    File.Move(temporaryTarget, targetPath, true);
                }
                catch (Exception exception) when (entry.IsEncrypted && IsPasswordFailure(exception))
                {
                    throw CreatePasswordException(password, exception);
                }
                finally
                {
                    if (File.Exists(temporaryTarget)) File.Delete(temporaryTarget);
                }
                if (entry.LastModifiedTime.HasValue)
                {
                    File.SetLastWriteTime(targetPath, entry.LastModifiedTime.Value);
                }
            }
            progress?.Report(new ArchiveProgress(100, string.Empty));
        }, cancellationToken);
    }

    private static async Task ExtractRawAsync(string archivePath, string destinationPath, FileDetector.FileType type, OverwritePolicy overwritePolicy, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        string entryName = GetRawOutputName(archivePath);
        string targetPath = GetSafeTargetPath(destinationPath, entryName);
        if (File.Exists(targetPath) && overwritePolicy == OverwritePolicy.SkipAll)
        {
            progress?.Report(new ArchiveProgress(100, entryName));
            return;
        }
        await using FileStream input = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        await using FileStream output = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        await using ProgressStream monitoredInput = new(input, input.Length, entryName, progress);
        await using Stream decoder = CreateDecoder(monitoredInput, type);
        await decoder.CopyToAsync(output, cancellationToken);
        progress?.Report(new ArchiveProgress(100, entryName));
    }

    private static void ExtractIso(string archivePath, string destinationPath, IReadOnlyCollection<string>? selectedEntries, OverwritePolicy overwritePolicy, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        using FileStream stream = File.OpenRead(archivePath);
        using CDReader reader = new(stream, true);
        string[] files = reader.GetFiles("", "*", SearchOption.AllDirectories).Where(path => ShouldInclude(path, selectedEntries)).ToArray();
        long total = Math.Max(files.Sum(reader.GetFileLength), 1);
        long completed = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = NormalizeEntry(file);
            string targetPath = GetSafeTargetPath(destinationPath, key);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            if (File.Exists(targetPath) && overwritePolicy == OverwritePolicy.SkipAll)
            {
                completed += reader.GetFileLength(file);
                continue;
            }
            using Stream input = reader.OpenFile(file, FileMode.Open);
            using FileStream output = File.Create(targetPath);
            CopyWithProgress(input, output, reader.GetFileLength(file), () => completed, value => completed += value, total, key, progress, cancellationToken);
        }
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static void ExtractWim(string archivePath, string destinationPath, IReadOnlyCollection<string>? selectedEntries, OverwritePolicy overwritePolicy, IProgress<ArchiveProgress>? progress)
    {
        EnsureWimInitialized();
        string stagingPath = Path.Combine(Path.GetTempPath(), "TangerineZipWim", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);
        try
        {
            using Wim wim = Wim.OpenWim(archivePath, OpenFlags.None);
            wim.RegisterCallback((message, info, _) =>
            {
                if (message == ProgressMsg.ExtractStreams && info is ExtractProgress extract)
                {
                    int percent = extract.TotalBytes == 0 ? 0 : (int)(extract.CompletedBytes * 90 / extract.TotalBytes);
                    progress?.Report(new ArchiveProgress(percent, extract.Target ?? string.Empty));
                }
                return CallbackStatus.Continue;
            });
            WimInfo wimInfo = wim.GetWimInfo();
            if (selectedEntries is null || selectedEntries.Count == 0)
            {
                for (int image = 1; image <= wimInfo.ImageCount; image++)
                {
                    string imageDestination = wimInfo.ImageCount == 1 ? stagingPath : Path.Combine(stagingPath, $"Image{image}");
                    Directory.CreateDirectory(imageDestination);
                    wim.ExtractImage(image, imageDestination, ExtractFlags.ReplaceInvalidFileNames);
                }
            }
            else
            {
                for (int image = 1; image <= wimInfo.ImageCount; image++)
                {
                    string prefix = $"{LanguageManager.Get("WimImage")} {image} - {wim.GetImageName(image)}/";
                    string[] paths = selectedEntries.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(path => "\\" + path[prefix.Length..].Replace('/', '\\')).Where(path => path != "\\").ToArray();
                    if (paths.Length > 0) wim.ExtractPaths(image, stagingPath, paths, ExtractFlags.ReplaceInvalidFileNames);
                }
            }
            MergeDirectory(stagingPath, destinationPath, overwritePolicy);
            progress?.Report(new ArchiveProgress(100, string.Empty));
        }
        finally
        {
            if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);
        }
    }

    private static void MergeDirectory(string sourcePath, string destinationPath, OverwritePolicy overwritePolicy)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, directory)));
        foreach (string file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string target = GetSafeTargetPath(destinationPath, Path.GetRelativePath(sourcePath, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && overwritePolicy == OverwritePolicy.SkipAll) continue;
            File.Copy(file, target, true);
        }
    }

    private static async Task CreateSharpCompressAsync(IReadOnlyList<string> sourcePaths, string outputPath, FileDetector.FileType type, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        ArchiveType archiveType = ArchiveType.SevenZip;
        SharpCompress.Common.CompressionType compressionType = SharpCompress.Common.CompressionType.LZMA2;
        long total = Math.Max(sourcePaths.SelectMany(EnumerateFiles).Sum(path => new FileInfo(path).Length), 1);
        long completed = 0;
        await using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        WriterOptions writerOptions = new(compressionType)
        {
            ArchiveEncoding = UnicodeArchiveEncoding
        };
        await using IAsyncWriter writer = await WriterFactory.OpenAsyncWriter(output, archiveType, writerOptions, cancellationToken);
        foreach (string sourcePath in sourcePaths)
        {
            string rootName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (string file in EnumerateFiles(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entryKey = File.Exists(sourcePath) ? rootName : NormalizeEntry(Path.Combine(rootName, Path.GetRelativePath(sourcePath, file)));
                await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
                long fileLength = input.Length;
                long completedBeforeEntry = completed;
                InlineProgress<ArchiveProgress> entryProgress = new(item =>
                {
                    long absolute = completedBeforeEntry + item.Percentage * fileLength / 100;
                    progress?.Report(new ArchiveProgress((int)Math.Clamp(absolute * 100 / total, 0, 100), entryKey));
                });
                await using ProgressStream monitoredInput = new(input, fileLength, entryKey, entryProgress);
                await writer.WriteAsync(entryKey, monitoredInput, File.GetLastWriteTime(file), cancellationToken);
                completed += fileLength;
            }
        }
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static async Task CreateZipAsync(IReadOnlyList<string> sourcePaths, string outputPath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        long total = Math.Max(sourcePaths.SelectMany(EnumerateFiles).Sum(path => new FileInfo(path).Length), 1);
        long completed = 0;
        await using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        // Passing UTF-8 explicitly makes ZipArchive set the language encoding flag for every entry.
        using System.IO.Compression.ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8);
        foreach (string sourcePath in sourcePaths)
        {
            string rootName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (string file in EnumerateFiles(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entryKey = File.Exists(sourcePath) ? rootName : NormalizeEntry(Path.Combine(rootName, Path.GetRelativePath(sourcePath, file)));
                ZipArchiveEntry entry = archive.CreateEntry(entryKey, System.IO.Compression.CompressionLevel.SmallestSize);
                DateTime lastWriteTime = File.GetLastWriteTime(file);
                if (lastWriteTime.Year is >= 1980 and <= 2107) entry.LastWriteTime = lastWriteTime;
                await using Stream entryStream = entry.Open();
                await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
                long fileLength = input.Length;
                long completedBeforeEntry = completed;
                InlineProgress<ArchiveProgress> entryProgress = new(item =>
                {
                    long absolute = completedBeforeEntry + item.Percentage * fileLength / 100;
                    progress?.Report(new ArchiveProgress((int)Math.Clamp(absolute * 100 / total, 0, 100), entryKey));
                });
                await using ProgressStream monitoredInput = new(input, fileLength, entryKey, entryProgress);
                await monitoredInput.CopyToAsync(entryStream, cancellationToken);
                completed += fileLength;
            }
        }
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static async Task CreateTarAsync(IReadOnlyList<string> sourcePaths, string outputPath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        long total = Math.Max(sourcePaths.SelectMany(EnumerateFiles).Sum(path => new FileInfo(path).Length), 1);
        long completed = 0;
        await using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        await using System.Formats.Tar.TarWriter writer = new(output, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true);
        foreach (string sourcePath in sourcePaths)
        {
            string rootName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            foreach (string file in EnumerateFiles(sourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entryKey = File.Exists(sourcePath) ? rootName : NormalizeEntry(Path.Combine(rootName, Path.GetRelativePath(sourcePath, file)));
                await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
                long fileLength = input.Length;
                long completedBeforeEntry = completed;
                InlineProgress<ArchiveProgress> entryProgress = new(item =>
                {
                    long absolute = completedBeforeEntry + item.Percentage * fileLength / 100;
                    progress?.Report(new ArchiveProgress((int)Math.Clamp(absolute * 100 / total, 0, 100), entryKey));
                });
                await using ProgressStream monitoredInput = new(input, fileLength, entryKey, entryProgress);
                System.Formats.Tar.PaxTarEntry entry = new(System.Formats.Tar.TarEntryType.RegularFile, entryKey)
                {
                    DataStream = monitoredInput,
                    ModificationTime = File.GetLastWriteTimeUtc(file)
                };
                await writer.WriteEntryAsync(entry, cancellationToken);
                completed += fileLength;
            }
        }
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static async Task CreateRawAsync(string sourcePath, string outputPath, FileDetector.FileType type, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new StageException("ARCSV0007", LanguageManager.Get("SingleInputRequired")); //ARCSV0007
        }
        await using FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        await using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        await using Stream encoder = CreateEncoder(output, type);
        await using ProgressStream monitoredInput = new(input, input.Length, Path.GetFileName(sourcePath), progress);
        await monitoredInput.CopyToAsync(encoder, cancellationToken);
        await encoder.FlushAsync(cancellationToken);
        progress?.Report(new ArchiveProgress(100, Path.GetFileName(sourcePath)));
    }

    private static void CreateIso(IReadOnlyList<string> sourcePaths, string outputPath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        CDBuilder builder = new() { UseJoliet = true, VolumeIdentifier = "TANGERINE_ZIP" };
        string[] files = sourcePaths.SelectMany(EnumerateFiles).ToArray();
        for (int index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourceRoot = sourcePaths.First(path => File.Exists(path) ? path == files[index] : files[index].StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            string entryKey = File.Exists(sourceRoot) ? Path.GetFileName(files[index]) : Path.Combine(Path.GetFileName(sourceRoot), Path.GetRelativePath(sourceRoot, files[index]));
            builder.AddFile(entryKey, files[index]);
            progress?.Report(new ArchiveProgress((index + 1) * 90 / Math.Max(files.Length, 1), entryKey));
        }
        builder.Build(outputPath);
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static void CreateWim(IReadOnlyList<string> sourcePaths, string outputPath, IProgress<ArchiveProgress>? progress)
    {
        EnsureWimInitialized();
        using Wim wim = Wim.CreateNewWim(ManagedWimLib.CompressionType.LZMS);
        wim.RegisterCallback((message, info, _) =>
        {
            if (message == ProgressMsg.WriteStreams && info is WriteStreamsProgress write)
            {
                int percent = write.TotalBytes == 0 ? 0 : (int)(write.CompletedBytes * 100 / write.TotalBytes);
                progress?.Report(new ArchiveProgress(percent, string.Empty));
            }
            return CallbackStatus.Continue;
        });
        int image = wim.AddEmptyImage("Tangerine ZIP");
        foreach (string path in sourcePaths)
            wim.AddTree(image, path, "\\" + Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), AddFlags.None);
        wim.Write(outputPath, Wim.AllImages, WriteFlags.CheckIntegrity, 0);
        progress?.Report(new ArchiveProgress(100, string.Empty));
    }

    private static Stream CreateDecoder(Stream input, FileDetector.FileType type)
    {
        return type switch
        {
            FileDetector.FileType.GZip => new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress, false),
            FileDetector.FileType.BZip2 => BZip2Stream.Create(input, SharpCompress.Compressors.CompressionMode.Decompress, false, false, true),
            FileDetector.FileType.Lz4 => LZ4Stream.Decode(input, leaveOpen: false),
            FileDetector.FileType.Xz => CreateXzDecoder(input),
            FileDetector.FileType.Zstd => new DecompressionStream(input, 1024 * 128, false, false),
            _ => throw new NotSupportedException()
        };
    }

    private static Stream CreateEncoder(Stream output, FileDetector.FileType type)
    {
        return type switch
        {
            FileDetector.FileType.GZip => new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.SmallestSize, false),
            FileDetector.FileType.BZip2 => BZip2Stream.Create(output, SharpCompress.Compressors.CompressionMode.Compress, false, false, true),
            FileDetector.FileType.Lz4 => LZ4Stream.Encode(output, K4os.Compression.LZ4.LZ4Level.L12_MAX, leaveOpen: false),
            FileDetector.FileType.Xz => CreateXzEncoder(output),
            FileDetector.FileType.Zstd => new CompressionStream(output, 9, 1024 * 128, false),
            _ => throw new NotSupportedException()
        };
    }

    private static Stream CreateXzEncoder(Stream output)
    {
        EnsureXzInitialized();
        return new XZStream(output, new XZCompressOptions { Level = LzmaCompLevel.Level9, LeaveOpen = false });
    }

    private static Stream CreateXzDecoder(Stream input)
    {
        EnsureXzInitialized();
        return new XZStream(input, new XZDecompressOptions { LeaveOpen = false });
    }

    private static void EnsureXzInitialized()
    {
        lock (NativeInitializationLock)
        {
            if (!_xzInitialized)
            {
                string? libraryPath = FindNativeLibrary("liblzma.dll");
                if (libraryPath is null) XZInit.GlobalInit(); else XZInit.GlobalInit(libraryPath);
                _xzInitialized = true;
            }
        }
    }

    private static void EnsureWimInitialized()
    {
        lock (NativeInitializationLock)
        {
            if (!_wimInitialized)
            {
                string? libraryPath = FindNativeLibrary("libwim-15.dll");
                if (libraryPath is null) Wim.GlobalInit(); else Wim.GlobalInit(libraryPath);
                _wimInitialized = true;
            }
        }
    }

    private static string? FindNativeLibrary(string fileName)
    {
        List<string> directories = [AppContext.BaseDirectory];
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string searchDirectories)
            directories.AddRange(searchDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        return directories.Select(directory => Path.Combine(directory, fileName)).FirstOrDefault(File.Exists);
    }

    private static async Task MaterializeNestedTarAsync(string archivePath, string tarEntryKey, string outputPath, CancellationToken cancellationToken, string? password)
    {
        FileDetector.FileType outerType = FileDetector.DetectFileType(archivePath);
        await using FileStream output = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true);
        if (ArchiveCapabilities.IsSingleFileStream(outerType))
        {
            await using FileStream input = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
            await using Stream decoder = CreateDecoder(input, outerType);
            await decoder.CopyToAsync(output, cancellationToken);
            return;
        }

        using IArchive archive = ArchiveFactory.OpenArchive(archivePath, CreateReaderOptions(password));
        IArchiveEntry? targetEntry = archive.Entries.FirstOrDefault(entry =>
            !entry.IsDirectory && NormalizeEntry(entry.Key ?? string.Empty).Equals(NormalizeEntry(tarEntryKey), StringComparison.OrdinalIgnoreCase));
        if (targetEntry is null)
        {
            throw new StageException("NESTR0004", LanguageManager.Get("NestedTarNotFound")); //NESTR0004
        }
        if (targetEntry.IsEncrypted && string.IsNullOrEmpty(password))
            throw new StageException("PWDAR0001", LanguageManager.Get("ArchivePasswordRequired")); //PWDAR0001
        try
        {
            await using Stream entryStream = targetEntry.OpenEntryStream();
            await entryStream.CopyToAsync(output, cancellationToken);
        }
        catch (Exception exception) when (targetEntry.IsEncrypted && IsPasswordFailure(exception))
        {
            throw CreatePasswordException(password, exception);
        }
    }

    private static StageException CreatePasswordException(string? password, Exception exception) =>
        string.IsNullOrEmpty(password)
            ? new StageException("PWDAR0001", LanguageManager.Get("ArchivePasswordRequired"), exception) //PWDAR0001
            : new StageException("PWDAR0002", LanguageManager.Get("ArchivePasswordInvalid"), exception); //PWDAR0002

    private static bool IsPasswordFailure(Exception exception)
    {
        if (exception is StageException stageException)
            return stageException.StageCode is "PWDAR0001" or "PWDAR0002";
        if (exception is System.Security.Cryptography.CryptographicException) return true;
        string message = exception.Message;
        return message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("decrypt", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("authentication", StringComparison.OrdinalIgnoreCase);
    }

    private static void DrainForAuthentication(Stream stream, string entryKey, long total, ref long completed,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 128];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) return;
            completed += read;
            progress?.Report(new ArchiveProgress((int)(completed * 100 / total), entryKey));
        }
    }

    private static string CreateTemporaryPath(string fileName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "TangerineZipNested", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static void DeleteTemporaryFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (File.Exists(path)) File.Delete(path);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static IEnumerable<string> EnumerateFiles(string path) => File.Exists(path) ? [path] : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);

    private static bool ShouldInclude(string key, IReadOnlyCollection<string>? selectedEntries)
    {
        if (selectedEntries is null || selectedEntries.Count == 0)
        {
            return true;
        }
        string normalized = NormalizeEntry(key);
        return selectedEntries.Any(selected => normalized.Equals(selected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(selected.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeEntry(string key) => key.Replace('\\', '/').TrimStart('/');

    private static string GetSafeTargetPath(string destinationPath, string entryKey)
    {
        string root = Path.GetFullPath(destinationPath) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(root, NormalizeEntry(entryKey).Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new StageException("ARCSV0008", LanguageManager.Get("UnsafeArchivePath")); //ARCSV0008
        }
        return target;
    }

    private static string GetRawOutputName(string archivePath)
    {
        string name = Path.GetFileNameWithoutExtension(archivePath);
        return string.IsNullOrWhiteSpace(name) ? LanguageManager.Get("ExtractedFileName") : name;
    }

    private static void CopyWithProgress(Stream input, Stream output, long entryLength, Func<long> getCompleted, Action<long> addCompleted, long total, string key, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 128];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            addCompleted(read);
            progress?.Report(new ArchiveProgress((int)Math.Clamp(getCompleted() * 100 / total, 0, 100), key));
        }
        if (entryLength == 0)
        {
            progress?.Report(new ArchiveProgress((int)Math.Clamp(getCompleted() * 100 / total, 0, 100), key));
        }
    }
}
