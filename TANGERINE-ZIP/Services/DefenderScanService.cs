using System.Diagnostics;
using System.Text;

namespace TANGERINE_ZIP.Services;

// Stage head: DSCAN. Defender is an optional system component. A custom scan
// checks the archive file without asking Defender to quarantine or modify it.
// The app never treats an unavailable engine or a failed scan as a clean result.
internal static class DefenderScanService
{
    public static async Task ScanAsync(string archivePath, CancellationToken token)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(archivePath); }
        catch (Exception error)
        {
            throw new StageException("DSCAN0001", LanguageManager.Get("DefenderTargetMissing"), error); //DSCAN0001
        }
        if (!File.Exists(fullPath))
            throw new StageException("DSCAN0001", LanguageManager.Get("DefenderTargetMissing")); //DSCAN0001

        string executable;
        try
        {
            executable = FindExecutable() ??
                throw new StageException("DSCAN0002", LanguageManager.Get("DefenderUnavailable")); //DSCAN0002
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("DSCAN0002", LanguageManager.Get("DefenderUnavailable"), error); //DSCAN0002
        }
        using Process process = new();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in new[] { "-Scan", "-ScanType", "3", "-File", fullPath, "-DisableRemediation" })
            process.StartInfo.ArgumentList.Add(argument);
        bool started = false;
        try
        {
            if (!process.Start())
                throw new StageException("DSCAN0003", LanguageManager.Get("DefenderStartFailed")); //DSCAN0003
            started = true;
            // Drain both redirected streams while the scanner runs. Waiting
            // before reading can deadlock when a detailed engine log fills a pipe.
            Task<string> output = process.StandardOutput.ReadToEndAsync(token);
            Task<string> errors = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            await Task.WhenAll(output, errors);
            if (process.ExitCode != 0)
            {
                // With -DisableRemediation, Defender may show detections only
                // in its command output. Preserve a bounded diagnostic for the
                // user rather than referring them to an empty Security log.
                string detail = (output.Result + Environment.NewLine + errors.Result).Trim();
                if (detail.Length > 4096) detail = detail[..4096];
                throw new StageException("DSCAN0004", LanguageManager.Get("DefenderScanFailed") +
                    " (" + process.ExitCode + ")" + (detail.Length == 0 ? string.Empty :
                    Environment.NewLine + detail)); //DSCAN0004
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (Exception error)
            {
                throw new StageException("DSCAN0005", LanguageManager.Get("DefenderStopFailed"), error); //DSCAN0005
            }
            throw;
        }
        catch (StageException) { throw; }
        catch (Exception error)
        {
            throw new StageException("DSCAN0003", LanguageManager.Get("DefenderStartFailed"), error); //DSCAN0003
        }
        finally
        {
            // A protocol or read failure must not leave the scanner running.
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                catch (Exception error)
                {
                    Trace.TraceError("DSCAN0005: {0}", error);
                }
            }
        }
    }

    private static string? FindExecutable()
    {
        string platformRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows Defender", "Platform");
        if (Directory.Exists(platformRoot))
        {
            // Platform directories use version numbers. Prefer the newest
            // installed engine while ignoring a partially removed directory.
            foreach (string folder in Directory.GetDirectories(platformRoot)
                .OrderByDescending(folder => Version.TryParse(Path.GetFileName(folder), out Version? version)
                    ? version : new Version(0, 0)))
            {
                string candidate = Path.Combine(folder, "MpCmdRun.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Windows Defender", "MpCmdRun.exe");
        return File.Exists(fallback) ? fallback : null;
    }
}
