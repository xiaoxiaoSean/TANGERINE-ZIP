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
    private const string LicenseResource = "TANGERINE_ZIP.SevenZip.License.txt";

    public async Task CreateEncryptedAsync(IReadOnlyList<string> sourcePaths, string outputPath,
        FileDetector.FileType type, string password, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        if (type is not (FileDetector.FileType.Zip or FileDetector.FileType.SevenZip))
            throw new StageException("SZTLS0001", LanguageManager.Get("PasswordFormatUnsupported")); //SZTLS0001
        if (string.IsNullOrEmpty(password))
            throw new StageException("SZTLS0002", LanguageManager.Get("ArchivePasswordRequired")); //SZTLS0002
        if (sourcePaths.Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)))
            throw new StageException("SZTLS0008", LanguageManager.Get("OutputConflictsInput")); //SZTLS0008

        string toolDirectory = await EnsureToolAsync(token);
        string temporaryOutput = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
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
            start.ArgumentList.Add(type == FileDetector.FileType.Zip ? "-mem=AES256" : "-mhe=on");
            start.ArgumentList.Add("-mmt=on");
            start.ArgumentList.Add("-bsp1");
            start.ArgumentList.Add("-sccUTF-8");
            start.ArgumentList.Add("-y");
            // 7-Zip has no redirected-stdin password switch. ArgumentList avoids shell parsing, although
            // Windows process inspection by the same user can still observe this argument while it runs.
            start.ArgumentList.Add("-p" + password);
            start.ArgumentList.Add(temporaryOutput);
            foreach (string sourcePath in sourcePaths) start.ArgumentList.Add(sourcePath);

            using Process process = new() { StartInfo = start };
            if (!process.Start()) throw new StageException("SZTLS0003", LanguageManager.Get("SevenZipStartFailed")); //SZTLS0003
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
            File.Move(temporaryOutput, outputPath, true);
            progress?.Report(new ArchiveProgress(100, string.Empty));
        }
        catch (StageException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { throw new StageException("SZTLS0005", exception.Message, exception); } //SZTLS0005
        finally
        {
            try { if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput); }
            catch (Exception exception) { throw new StageException("SZTLS0006", LanguageManager.Get("ArchivePasswordCleanupFailed"), exception); } //SZTLS0006
        }
    }

    private static async Task<string> EnsureToolAsync(CancellationToken token)
    {
        Assembly assembly = typeof(SevenZipToolService).Assembly;
        string identity;
        await using (Stream source = assembly.GetManifestResourceStream(ExecutableResource)
            ?? throw new StageException("SZTLS0007", LanguageManager.Get("SevenZipEmbeddedMissing"))) //SZTLS0007
        {
            identity = Convert.ToHexString(SHA256.HashData(source)).Substring(0, 16);
        }
        string directory = Path.Combine(Path.GetTempPath(), "TangerineZip7Zip", identity);
        Directory.CreateDirectory(directory);
        await ExtractResourceAsync(assembly, ExecutableResource, Path.Combine(directory, "7z.exe"), token);
        await ExtractResourceAsync(assembly, LibraryResource, Path.Combine(directory, "7z.dll"), token);
        await ExtractResourceAsync(assembly, LicenseResource, Path.Combine(directory, "License.txt"), token);
        return directory;
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
