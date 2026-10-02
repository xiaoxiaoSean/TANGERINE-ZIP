using System.Diagnostics;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common.Options;
using SharpCompress.Readers;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: ARUPD. An existing archive is never changed until a complete
// replacement has been checked and atomically installed with a backup.
internal static class ArchiveUpdateService
{
    public static async Task<string> AddFilesAsync(string archivePath, IReadOnlyList<string> sources,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (sources.Count == 0)
            throw new StageException("ARUPD0001", LanguageManager.Get("ArchiveUpdateNoFiles")); //ARUPD0001

        string archive = Path.GetFullPath(archivePath);
        FileDetector.FileType type = FileDetector.DetectFileType(archive);
        if (type is not (FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Rar) ||
            archive.EndsWith(".001", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(archive, @"\.part\d+\.rar$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            File.Exists(archive + ".001") || File.Exists(Path.ChangeExtension(archive, ".z01")) ||
            File.Exists(Path.ChangeExtension(archive, ".r00")))
            throw new StageException("ARUPD0002", LanguageManager.Get("ArchiveUpdateUnsupported")); //ARUPD0002
        if (type == FileDetector.FileType.Rar && !new RarToolService().IsAvailable)
            throw new StageException("RARTL0001", LanguageManager.Get("RarToolMissing")); //RARTL0001

        // A named source is added at the archive root. Reject ambiguous source
        // names and self-inclusion before creating any temporary output.
        string[] fullSources = sources.Select(Path.GetFullPath).ToArray();
        if (fullSources.Any(source => !File.Exists(source)) ||
            fullSources.Any(source => source.Equals(archive, StringComparison.OrdinalIgnoreCase)))
            throw new StageException("ARUPD0003", LanguageManager.Get("ArchiveUpdateSourceMissing")); //ARUPD0003
        if (fullSources.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fullSources.Length)
            throw new StageException("ARUPD0004", LanguageManager.Get("ArchiveUpdateDuplicateNames")); //ARUPD0004

        string workingCopy = archive + "." + Guid.NewGuid().ToString("N") + ".updating";
        string backup = archive + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "." +
            Guid.NewGuid().ToString("N") + ".bak";
        string? toolDirectory = null;
        Exception? operationError = null;
        try
        {
            // Neither the embedded 7-Zip tool nor optional RAR tool receives
            // a password here: editing encrypted archives could silently mix
            // protected and unprotected members. Reject them before copying.
            HashSet<string> existingRootNames = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                using IArchive inspection = ArchiveFactory.OpenArchive(archive, new ReaderOptions());
                if (inspection.Entries.Any(entry => !entry.IsDirectory && entry.IsEncrypted))
                    throw new StageException("ARUPD0002", LanguageManager.Get("ArchiveUpdateUnsupported")); //ARUPD0002
                foreach (var entry in inspection.Entries.Where(entry => !entry.IsDirectory))
                {
                    string key = (entry.Key ?? string.Empty).Replace('\\', '/');
                    if (!key.Contains('/') && !string.IsNullOrEmpty(key))
                        existingRootNames.Add(key);
                }
            }
            catch (StageException) { throw; }
            catch (Exception error) when (type == FileDetector.FileType.Rar)
            {
                // Header-encrypted RAR files can fail before entry metadata
                // becomes visible. They are intentionally outside update scope.
                throw new StageException("ARUPD0002", LanguageManager.Get("ArchiveUpdateUnsupported"), error); //ARUPD0002
            }

            long inputBytes = 0;
            foreach (string source in fullSources)
            {
                long length = new FileInfo(source).Length;
                inputBytes = inputBytes > long.MaxValue - length ? long.MaxValue : inputBytes + length;
            }
            long archiveBytes = new FileInfo(archive).Length;
            long required = archiveBytes > (long.MaxValue - inputBytes) / 2
                ? long.MaxValue : archiveBytes * 2 + inputBytes;
            ResourcePreflight.Check(archive, required, 128L * 1024 * 1024);

            (long Length, DateTime LastWriteUtc) identity = Identity(archive);
            File.Copy(archive, workingCopy, overwrite: false);
            if (type != FileDetector.FileType.Rar)
                toolDirectory = await SevenZipToolService.EnsureToolAsync(token);
            for (int index = 0; index < fullSources.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                string source = fullSources[index];
                string sourceName = Path.GetFileName(source);
                // 7-Zip's default add/update mode can retain an older member
                // when the replacement source has an earlier timestamp. Remove
                // only an exact root member from the private working copy first.
                if (type != FileDetector.FileType.Rar && existingRootNames.Contains(sourceName))
                    await RunToolAsync("d", sourceName, Path.GetDirectoryName(source)!);
                await RunToolAsync("a", type == FileDetector.FileType.Rar ? source : sourceName,
                    Path.GetDirectoryName(source)!);
                progress?.Report(new ArchiveProgress((index + 1) * 90 / fullSources.Length, sourceName));
            }
            token.ThrowIfCancellationRequested();
            if (Identity(archive) != identity)
                throw new StageException("ARUPD0006", LanguageManager.Get("ArchiveEditChanged")); //ARUPD0006
            if (FileDetector.DetectFileType(workingCopy) != type)
                throw new StageException("ARUPD0007", LanguageManager.Get("ArchiveUpdateInvalidOutput")); //ARUPD0007
            File.Replace(workingCopy, archive, backup, ignoreMetadataErrors: false);
            progress?.Report(new ArchiveProgress(100, string.Empty));
            return backup;

            async Task RunToolAsync(string action, string sourceName, string sourceDirectory)
            {
                ProcessStartInfo start = new(type == FileDetector.FileType.Rar
                    ? new RarToolService().ExecutablePath : Path.Combine(toolDirectory!, "7z.exe"))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = sourceDirectory
                };
                start.ArgumentList.Add(action);
                if (type == FileDetector.FileType.Rar)
                    start.ArgumentList.Add("-ep1");
                else
                {
                    start.ArgumentList.Add(type == FileDetector.FileType.Zip ? "-tzip" : "-t7z");
                    start.ArgumentList.Add("-spd");
                }
                start.ArgumentList.Add("-y");
                start.ArgumentList.Add(workingCopy);
                if (type != FileDetector.FileType.Rar) start.ArgumentList.Add("--");
                start.ArgumentList.Add(sourceName);
                using Process process = new() { StartInfo = start };
                if (!process.Start())
                    throw new StageException("ARUPD0005", LanguageManager.Get("ArchiveUpdateToolFailed")); //ARUPD0005
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(token);
                try { await process.WaitForExitAsync(token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    throw;
                }
                string diagnostics = (await stdout) + Environment.NewLine + (await stderr);
                if (process.ExitCode != 0)
                    throw new StageException("ARUPD0005", string.Format(
                        LanguageManager.Get("ArchiveUpdateToolExit"), process.ExitCode, diagnostics.Trim())); //ARUPD0005
            }
        }
        catch (StageException error) { operationError = error; throw; }
        catch (OperationCanceledException error) { operationError = error; throw; }
        catch (Exception error)
        {
            operationError = error;
            throw new StageException("ARUPD0008", LanguageManager.Get("ArchiveUpdateFailed"), error); //ARUPD0008
        }
        finally
        {
            try
            {
                if (File.Exists(workingCopy)) File.Delete(workingCopy);
                if (toolDirectory is not null && Directory.Exists(toolDirectory))
                    Directory.Delete(toolDirectory, recursive: true);
            }
            catch (Exception cleanupError)
            {
                if (operationError is not null) operationError.Data["CleanupError"] = cleanupError;
                else throw new StageException("ARUPD0009", LanguageManager.Get("ArchiveUpdateCleanupFailed"), cleanupError); //ARUPD0009
            }
        }
    }

    private static (long Length, DateTime LastWriteUtc) Identity(string path)
    {
        FileInfo file = new(path);
        file.Refresh();
        return (file.Length, file.LastWriteTimeUtc);
    }
}
