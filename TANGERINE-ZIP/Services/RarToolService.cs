using System.Diagnostics;
using System.Text;

namespace TANGERINE_ZIP.Services;

internal sealed class RarToolService
{
    private const string ExecutableName = "rar.exe";
    private const string SkipStartupCheckMarkerName = "DONT_CHECK_RAR_EXE_AT_START";

    public string ExecutablePath => Path.Combine(AppContext.BaseDirectory, ExecutableName);

    public string SkipStartupCheckMarkerPath => Path.Combine(AppContext.BaseDirectory, SkipStartupCheckMarkerName);

    public bool IsAvailable => File.Exists(ExecutablePath);

    // The marker suppresses only the startup warning. RAR creation always checks rar.exe again.
    public bool ShouldCheckAtStartup => !File.Exists(SkipStartupCheckMarkerPath);

    public async Task CreateAsync(
        IReadOnlyList<string> sourcePaths,
        string outputPath,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new StageException("RARTL0001", LanguageManager.Get("RarToolMissing")); //RARTL0001
        }

        string temporaryOutputPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".rar";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourcePaths.Any(path => Path.GetFullPath(path).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)))
                throw new StageException("RARTL0006", LanguageManager.Get("OutputConflictsInput")); //RARTL0006
            ProcessStartInfo startInfo = new()
            {
                FileName = ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(sourcePaths[0]) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("a");
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add("-ep1");
            startInfo.ArgumentList.Add("-m5");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(temporaryOutputPath);
            foreach (string sourcePath in sourcePaths)
            {
                startInfo.ArgumentList.Add(sourcePath);
            }

            using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                throw new StageException("RARTL0002", LanguageManager.Get("RarToolStartFailed")); //RARTL0002
            }
            // RAR refreshes percentages with backspaces, not necessarily with line breaks.
            // Read characters continuously and report only parsed percentages to avoid code-page-dependent UI text.
            Task<string> outputTask = ReadProgressAsync(process.StandardOutput, progress);
            Task<string> errorTask = ReadProgressAsync(process.StandardError, null);
            Task exitTask = process.WaitForExitAsync();
            Task cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (await Task.WhenAny(exitTask, cancellationTask) == cancellationTask)
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                    await process.WaitForExitAsync();
                    await Task.WhenAll(outputTask, errorTask);
                }
                catch (Exception exception)
                {
                    throw new StageException("RARTL0005", LanguageManager.Get("RarCancelFailed"), exception); //RARTL0005
                }
                throw new OperationCanceledException(cancellationToken);
            }
            await exitTask;
            string[] diagnostics = await Task.WhenAll(outputTask, errorTask);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                throw new StageException("RARTL0003", string.Format(LanguageManager.Get("RarToolExitCode"), process.ExitCode) + Environment.NewLine + string.Join(Environment.NewLine, diagnostics)); //RARTL0003
            }
            File.Move(temporaryOutputPath, outputPath, true);
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
            throw new StageException("RARTL0004", exception.Message, exception); //RARTL0004
        }
        finally
        {
            if (File.Exists(temporaryOutputPath)) File.Delete(temporaryOutputPath);
        }
    }

    private static async Task<string> ReadProgressAsync(StreamReader reader, IProgress<ArchiveProgress>? progress)
    {
        char[] buffer = new char[1024];
        StringBuilder diagnostics = new();
        int digits = 0;
        int value = 0;
        int highestPercentage = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            diagnostics.Append(buffer, 0, count);
            if (diagnostics.Length > 8192) diagnostics.Remove(0, diagnostics.Length - 8192);
            for (int index = 0; index < count; index++)
            {
                char character = buffer[index];
                if (character is >= '0' and <= '9')
                {
                    value = Math.Min(1000, value * 10 + character - '0');
                    digits++;
                    continue;
                }
                if (character == '%' && digits is > 0 and <= 3 && value <= 100)
                {
                    highestPercentage = Math.Max(highestPercentage, Math.Min(value, 99));
                    progress?.Report(new ArchiveProgress(highestPercentage, string.Empty));
                }
                digits = 0;
                value = 0;
            }
        }
        return diagnostics.ToString();
    }
}
