using ICSharpCode.SharpZipLib.Zip;

namespace TANGERINE_ZIP.Services;

// Stage head: ZIPPD
internal sealed class EncryptedZipService
{
    public async Task CreateAsync(IReadOnlyList<string> sourcePaths, string outputPath, string password,
        IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrEmpty(password))
            throw new StageException("ZIPPD0001", LanguageManager.Get("ArchivePasswordRequired")); //ZIPPD0001
        if (sourcePaths.Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)))
            throw new StageException("ZIPPD0004", LanguageManager.Get("OutputConflictsInput")); //ZIPPD0004
        string temporaryOutput = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string[] files = sourcePaths.SelectMany(path => File.Exists(path)
                ? [path]
                : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)).ToArray();
            long total = Math.Max(files.Sum(path => new FileInfo(path).Length), 1);
            long completed = 0;
            {
                await using FileStream fileOutput = new(temporaryOutput, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 128 * 1024, true);
                using ZipOutputStream zip = new(fileOutput) { IsStreamOwner = false, Password = password };
                zip.SetLevel(9);
                byte[] buffer = new byte[128 * 1024];
                foreach (string sourceRoot in sourcePaths)
                {
                    IEnumerable<string> sourceFiles = File.Exists(sourceRoot)
                        ? [sourceRoot]
                        : Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories);
                    foreach (string file in sourceFiles)
                    {
                        token.ThrowIfCancellationRequested();
                        string entryName = File.Exists(sourceRoot)
                            ? Path.GetFileName(file)
                            : Path.Combine(Path.GetFileName(sourceRoot), Path.GetRelativePath(sourceRoot, file));
                        entryName = entryName.Replace('\\', '/');
                        ZipEntry entry = new(entryName)
                        {
                            AESKeySize = 256,
                            DateTime = File.GetLastWriteTime(file),
                            Size = new FileInfo(file).Length,
                            IsUnicodeText = true
                        };
                        zip.PutNextEntry(entry);
                        await using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                            buffer.Length, true);
                        int count;
                        while ((count = await input.ReadAsync(buffer, token)) > 0)
                        {
                            await zip.WriteAsync(buffer.AsMemory(0, count), token);
                            completed += count;
                            progress?.Report(new ArchiveProgress((int)(completed * 99 / total), entryName));
                        }
                        zip.CloseEntry();
                    }
                }
                zip.Finish();
                await fileOutput.FlushAsync(token);
            }
            File.Move(temporaryOutput, outputPath, true);
            progress?.Report(new ArchiveProgress(100, string.Empty));
        }
        catch (StageException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw new StageException("ZIPPD0002", exception.Message, exception); } //ZIPPD0002
        finally
        {
            try { if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput); }
            catch (Exception exception) { throw new StageException("ZIPPD0003", LanguageManager.Get("ArchivePasswordCleanupFailed"), exception); } //ZIPPD0003
        }
    }
}
