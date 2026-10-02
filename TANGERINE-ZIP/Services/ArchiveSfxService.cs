using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: ARSFX. Every readable input is converted to 7z content before
// the embedded 7-Zip SFX module is attached. The source is never modified.
internal static class ArchiveSfxService
{
    public static async Task CreateAsync(string sourcePath, string outputPath, string? inputPassword,
        ArchiveWorkerClient worker, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        string source;
        string output;
        try
        {
            source = Path.GetFullPath(sourcePath);
            output = Path.GetFullPath(outputPath);
        }
        catch (Exception error)
        {
            throw new StageException("ARSFX0001", LanguageManager.Get("SfxSourceUnsupported"), error); //ARSFX0001
        }
        FileDetector.FileType sourceType;
        try { sourceType = File.Exists(source) ? FileDetector.DetectFileType(source) : FileDetector.FileType.Unknown; }
        catch (Exception error)
        {
            throw new StageException("ARSFX0001", LanguageManager.Get("SfxSourceUnsupported"), error); //ARSFX0001
        }
        if (!ArchiveCapabilities.CanOpen(sourceType))
            throw new StageException("ARSFX0001", LanguageManager.Get("SfxSourceUnsupported")); //ARSFX0001
        if (!output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            source.Equals(output, StringComparison.OrdinalIgnoreCase))
            throw new StageException("ARSFX0002", LanguageManager.Get("SfxOutputInvalid")); //ARSFX0002
        if (File.Exists(output) || Directory.Exists(output))
            throw new StageException("ARSFX0003", LanguageManager.Get("SfxOutputExists")); //ARSFX0003
        string? outputDirectory = Path.GetDirectoryName(output);
        if (outputDirectory is null || !Directory.Exists(outputDirectory))
            throw new StageException("ARSFX0002", LanguageManager.Get("SfxOutputInvalid")); //ARSFX0002

        FileInfo input = new(source);
        (long Length, DateTime WrittenUtc) sourceIdentity;
        try
        {
            // Reading metadata can fail even after the format probe succeeds,
            // for example when access is revoked while the dialog closes.
            sourceIdentity = (input.Length, input.LastWriteTimeUtc);
        }
        catch (Exception error)
        {
            throw new StageException("ARSFX0001", LanguageManager.Get("SfxSourceUnsupported"), error); //ARSFX0001
        }
        string workspace = TempDirectorySettings.GetDirectory();
        string extractionRoot = Path.Combine(workspace, "SfxConvert_" + Guid.NewGuid().ToString("N"));
        string stagedOutput = output + "." + Guid.NewGuid().ToString("N") + ".tmp.exe";
        Exception? operationError = null;
        try
        {
            // ArchiveBatchService applies the regular extraction path checks,
            // nested-TAR handling, and per-member issue policy. Its unique
            // parent keeps this operation separate from other concurrent jobs.
            Directory.CreateDirectory(extractionRoot);
            IProgress<ArchiveProgress> extracting = new InlineProgress<ArchiveProgress>(item =>
                progress?.Report(new ArchiveProgress(item.Percentage / 2, item.EntryKey)));
            await ArchiveBatchService.ExtractAsync([source], extractionRoot, inputPassword,
                worker, extracting, token,
                (_, _) => Task.FromResult(new ExtractionAnswer(ExtractionDecision.Stop)));
            input.Refresh();
            if ((input.Length, input.LastWriteTimeUtc) != sourceIdentity)
                throw new StageException("ARSFX0004", LanguageManager.Get("SfxSourceChanged")); //ARSFX0004

            string[] extractedFolders = Directory.GetDirectories(extractionRoot);
            if (extractedFolders.Length != 1)
                throw new StageException("ARSFX0005", LanguageManager.Get("SfxEmptyArchive")); //ARSFX0005
            string[] members = Directory.GetFileSystemEntries(extractedFolders[0]);
            if (members.Length == 0)
                throw new StageException("ARSFX0005", LanguageManager.Get("SfxEmptyArchive")); //ARSFX0005

            IProgress<ArchiveProgress> compressing = new InlineProgress<ArchiveProgress>(item =>
                progress?.Report(new ArchiveProgress(50 + item.Percentage / 2, item.EntryKey)));
            await worker.CreateAsync(members, stagedOutput, FileDetector.FileType.SevenZip,
                compressing, token, options: new CompressionOptions(Advanced: true, SelfExtracting: true));
            token.ThrowIfCancellationRequested();
            // Verify both the PE stub and attached 7z signature before
            // publishing the private sibling under the requested name.
            using (FileStream check = File.OpenRead(stagedOutput))
            {
                byte[] prefix = new byte[(int)Math.Min(check.Length, 8L * 1024 * 1024)];
                check.ReadExactly(prefix);
                ReadOnlySpan<byte> sevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
                if (prefix.Length < 32 || prefix[0] != 'M' || prefix[1] != 'Z' ||
                    prefix.AsSpan().IndexOf(sevenZipSignature) < 0)
                    throw new StageException("ARSFX0006", LanguageManager.Get("SfxInvalidOutput")); //ARSFX0006
            }
            File.Move(stagedOutput, output, overwrite: false);
            progress?.Report(new ArchiveProgress(100, Path.GetFileName(output)));
        }
        catch (StageException error) { operationError = error; throw; }
        catch (OperationCanceledException error) { operationError = error; throw; }
        catch (Exception error)
        {
            operationError = error;
            throw new StageException("ARSFX0007", LanguageManager.Get("SfxConversionFailed"), error); //ARSFX0007
        }
        finally
        {
            try
            {
                if (File.Exists(stagedOutput)) File.Delete(stagedOutput);
                // Do not recursively delete a path whose parent or final node
                // changed into a junction while a worker was extracting.
                string expectedParent = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
                string actualParent = Path.GetDirectoryName(Path.GetFullPath(extractionRoot))!
                    .TrimEnd(Path.DirectorySeparatorChar);
                if (!actualParent.Equals(expectedParent, StringComparison.OrdinalIgnoreCase) ||
                    Directory.Exists(extractionRoot) &&
                    File.GetAttributes(extractionRoot).HasFlag(FileAttributes.ReparsePoint))
                    throw new StageException("ARSFX0008", LanguageManager.Get("SfxCleanupFailed")); //ARSFX0008
                if (Directory.Exists(extractionRoot)) Directory.Delete(extractionRoot, recursive: true);
            }
            catch (Exception cleanupError)
            {
                if (operationError is not null) operationError.Data["CleanupError"] = cleanupError;
                else throw new StageException("ARSFX0008", LanguageManager.Get("SfxCleanupFailed"), cleanupError); //ARSFX0008
            }
        }
    }
}
