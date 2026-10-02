using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: SZTLS
// The official 7-Zip console components are embedded so the main application remains a single-file executable.
internal sealed class SevenZipToolService
{
    private const string ExecutableResource = "TANGERINE_ZIP.SevenZip.7z.exe";
    private const string LibraryResource = "TANGERINE_ZIP.SevenZip.7z.dll";
    private const string SfxResource = "TANGERINE_ZIP.SevenZip.7z.sfx";
    private const string LicenseResource = "TANGERINE_ZIP.SevenZip.License.txt";

    public async Task CreateEncryptedAsync(IReadOnlyList<string> sourcePaths, string outputPath,
        FileDetector.FileType type, string password, IProgress<ArchiveProgress>? progress, CancellationToken token)
        => await CreateConfiguredAsync(sourcePaths, outputPath, type, new CompressionOptions(password), progress, token);

    public async Task CreateConfiguredAsync(IReadOnlyList<string> sourcePaths, string outputPath,
        FileDetector.FileType type, CompressionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (type is not (FileDetector.FileType.Zip or FileDetector.FileType.SevenZip))
            throw new StageException("SZTLS0001", LanguageManager.Get("PasswordFormatUnsupported")); //SZTLS0001
        if (options.SelfExtracting && (type != FileDetector.FileType.SevenZip ||
            options.VolumeMiB > 0 || !outputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new StageException("SZTLS0012", LanguageManager.Get("SfxInvalidOptions")); //SZTLS0012
        if (options.SolidMode is not ("Default" or "On" or "Off") ||
            options.ExcludePatterns?.Any(pattern => string.IsNullOrWhiteSpace(pattern) ||
                pattern.Length > 260 || pattern.Contains('\r') || pattern.Contains('\n')) == true)
            throw new StageException("SZTLS0013", LanguageManager.Get("CompressionInvalidOptions")); //SZTLS0013
        if (options.Method == "LZMA2" && type == FileDetector.FileType.Zip ||
            options.Method == "Deflate" && type == FileDetector.FileType.SevenZip)
            throw new StageException("SZTLS0009", LanguageManager.Get("CompressionInvalidOptions"));
        if (sourcePaths.Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)))
            throw new StageException("SZTLS0008", LanguageManager.Get("OutputConflictsInput")); //SZTLS0008

        string toolDirectory;
        try { toolDirectory = await EnsureToolAsync(token); }
        catch (StageException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("SZTLS0005", exception.Message, exception); //SZTLS0005
        }
        string temporaryOutput = outputPath + "." + Guid.NewGuid().ToString("N") +
            (options.SelfExtracting ? ".tmp.exe" : ".tmp");
        List<string> movedVolumes = [];
        try
        {
            if (options.VolumeMiB > 0 && File.Exists(outputPath + ".001"))
                throw new StageException("SZTLS0011", LanguageManager.Get("CompressionVolumeExists"));
            ProcessStartInfo start = new(Path.Combine(toolDirectory, "7z.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(sourcePaths[0]) ?? AppContext.BaseDirectory
            };
            start.ArgumentList.Add("a");
            start.ArgumentList.Add(type == FileDetector.FileType.Zip ? "-tzip" : "-t7z");
            if (options.SelfExtracting) start.ArgumentList.Add("-sfx7z.sfx");
            if (!string.IsNullOrEmpty(options.Password))
                start.ArgumentList.Add(type == FileDetector.FileType.Zip ? "-mem=AES256" : "-mhe=on");
            start.ArgumentList.Add(options.Threads == 0 ? "-mmt=on" : $"-mmt={options.Threads}");
            if (options.Advanced)
            {
                start.ArgumentList.Add($"-mx={options.Level}");
                if (options.Method != "Default") start.ArgumentList.Add("-m0=" + options.Method);
                if (type == FileDetector.FileType.SevenZip) start.ArgumentList.Add($"-md={options.DictionaryMiB}m");
                if (type == FileDetector.FileType.SevenZip && options.SolidMode != "Default")
                    start.ArgumentList.Add(options.SolidMode == "On" ? "-ms=on" : "-ms=off");
                if (options.VolumeMiB > 0) start.ArgumentList.Add($"-v{options.VolumeMiB}m");
                foreach (string pattern in options.ExcludePatterns ?? [])
                    start.ArgumentList.Add("-xr!" + pattern);
            }
            start.ArgumentList.Add("-bsp1");
            start.ArgumentList.Add("-sccUTF-8");
            start.ArgumentList.Add("-y");
            // 7-Zip has no redirected-stdin password switch. ArgumentList avoids shell parsing, although
            // Windows process inspection by the same user can still observe this argument while it runs.
            if (!string.IsNullOrEmpty(options.Password)) start.ArgumentList.Add("-p" + options.Password);
            start.ArgumentList.Add(temporaryOutput);
            foreach (string sourcePath in sourcePaths) start.ArgumentList.Add(sourcePath);

            using Process process = new() { StartInfo = start };
            if (!process.Start()) throw new StageException("SZTLS0003", LanguageManager.Get("SevenZipStartFailed")); //SZTLS0003
            // 7-Zip 23.01 does not support -memuse. A Windows job object enforces the
            // configured process memory ceiling instead of silently ignoring it.
            using JobObjectMemoryLimit? memoryLimit = options.MemoryLimitMiB > 0
                ? JobObjectMemoryLimit.Attach(process, (long)options.MemoryLimitMiB * 1024 * 1024) : null;
            Task<string> outputTask = ReadProgressAsync(process.StandardOutput, progress);
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync();
                throw;
            }
            string outputText = await outputTask;
            string errorText = await errorTask;
            if (process.ExitCode != 0)
                throw new StageException("SZTLS0004", string.Format(LanguageManager.Get("SevenZipExitCode"), process.ExitCode) +
                    Environment.NewLine + outputText + Environment.NewLine + errorText); //SZTLS0004
            if (options.VolumeMiB > 0)
            {
                string[] parts = Directory.GetFiles(Path.GetDirectoryName(temporaryOutput)!, Path.GetFileName(temporaryOutput) + ".*")
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (parts.Length == 0) throw new StageException("SZTLS0010",
                    string.Format(LanguageManager.Get("CompressionOutputMissing"), outputPath));
                foreach (string part in parts)
                {
                    string destination = outputPath + part[temporaryOutput.Length..];
                    File.Move(part, destination);
                    movedVolumes.Add(destination);
                }
            }
            else File.Move(temporaryOutput, outputPath, true);
            progress?.Report(new ArchiveProgress(100, string.Empty));
        }
        catch (StageException) { DeleteMovedVolumes(); throw; }
        catch (OperationCanceledException) { DeleteMovedVolumes(); throw; }
        catch (Exception exception) { DeleteMovedVolumes(); throw new StageException("SZTLS0005", exception.Message, exception); } //SZTLS0005
        finally
        {
            try
            {
                if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
                foreach (string part in Directory.GetFiles(Path.GetDirectoryName(temporaryOutput)!, Path.GetFileName(temporaryOutput) + ".*")) File.Delete(part);
                // Each invocation owns a unique embedded-tool directory, so
                // removing it cannot disrupt a second compression process.
                if (Directory.Exists(toolDirectory)) Directory.Delete(toolDirectory, recursive: true);
            }
            catch (Exception exception) { throw new StageException("SZTLS0006", LanguageManager.Get("ArchivePasswordCleanupFailed"), exception); } //SZTLS0006
        }
        void DeleteMovedVolumes()
        {
            foreach (string part in movedVolumes) if (File.Exists(part)) File.Delete(part);
        }
    }

    // ArchiveUpdateService uses the same embedded executable, so both paths
    // share one extraction and cleanup contract instead of depending on a
    // separately installed 7-Zip copy.
    internal static async Task<string> EnsureToolAsync(CancellationToken token)
    {
        Assembly assembly = typeof(SevenZipToolService).Assembly;
        string identity;
        await using (Stream source = assembly.GetManifestResourceStream(ExecutableResource)
            ?? throw new StageException("SZTLS0007", LanguageManager.Get("SevenZipEmbeddedMissing"))) //SZTLS0007
        {
            identity = Convert.ToHexString(SHA256.HashData(source)).Substring(0, 16);
        }
        string directory = Path.Combine(TempDirectorySettings.GetDirectory(), "TangerineZip7Zip", identity + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await ExtractResourceAsync(assembly, ExecutableResource, Path.Combine(directory, "7z.exe"), token);
            await ExtractResourceAsync(assembly, LibraryResource, Path.Combine(directory, "7z.dll"), token);
            await ExtractResourceAsync(assembly, SfxResource, Path.Combine(directory, "7z.sfx"), token);
            await ExtractResourceAsync(assembly, LicenseResource, Path.Combine(directory, "License.txt"), token);
            return directory;
        }
        catch (Exception extractionError)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception cleanupError)
            {
                throw new StageException("SZTLS0006", LanguageManager.Get("ArchivePasswordCleanupFailed"),
                    new AggregateException(extractionError, cleanupError)); //SZTLS0006
            }
            throw;
        }
    }

    private static async Task ExtractResourceAsync(Assembly assembly, string resourceName, string targetPath, CancellationToken token)
    {
        if (File.Exists(targetPath)) return;
        string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using Stream source = assembly.GetManifestResourceStream(resourceName)
                ?? throw new StageException("SZTLS0007", LanguageManager.Get("SevenZipEmbeddedMissing")); //SZTLS0007
            await using (FileStream target = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await source.CopyToAsync(target, token);
            try { File.Move(temporaryPath, targetPath); }
            catch (IOException) when (File.Exists(targetPath)) { File.Delete(temporaryPath); }
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static async Task<string> ReadProgressAsync(StreamReader reader, IProgress<ArchiveProgress>? progress)
    {
        char[] buffer = new char[1024];
        StringBuilder text = new();
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            text.Append(buffer, 0, count);
            if (text.Length > 8192) text.Remove(0, text.Length - 8192);
            for (int index = 0; index < count - 2; index++)
            {
                if (!char.IsDigit(buffer[index])) continue;
                int end = index;
                while (end < count && char.IsDigit(buffer[end])) end++;
                if (end < count && buffer[end] == '%' && int.TryParse(buffer.AsSpan(index, end - index), out int value))
                    progress?.Report(new ArchiveProgress(Math.Clamp(value, 0, 99), string.Empty));
                index = end;
            }
        }
        return text.ToString();
    }
}
