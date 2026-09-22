using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// A separate instance of the same executable isolates native codecs and permits forced termination.
// JSON travels only through redirected pipes; user filenames are never shell command text.
internal sealed record WorkerRequest(string Operation, string Path, string? Destination = null,
    string[]? Sources = null, FileDetector.FileType Type = FileDetector.FileType.Unknown,
    string[]? TarKeys = null, string[]? Selection = null, bool Subfolders = false,
    OverwritePolicy Policy = OverwritePolicy.OverwriteAll, string Culture = "en-US", string? Password = null);

internal sealed record WorkerMessage(ArchiveProgress? Progress = null, ArchiveEntryInfo[]? Entries = null,
    NestedTarInfo? Nested = null, bool Completed = false, string? StageCode = null, string? Error = null);

internal static class ArchiveWorker
{
    public const string Switch = "--archive-worker";

    public static async Task<int> ExecuteAsync()
    {
        using StreamReader input = new(Console.OpenStandardInput(), Encoding.UTF8);
        using StreamWriter output = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        object writeLock = new();
        void Send(WorkerMessage message)
        {
            lock (writeLock) output.WriteLine(JsonSerializer.Serialize(message));
        }
        try
        {
            WorkerRequest request = JsonSerializer.Deserialize<WorkerRequest>(await input.ReadLineAsync() ?? "")
                ?? throw new InvalidDataException();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(request.Culture);
            ArchiveService service = new();
            long lastReport = 0;
            InlineProgress<ArchiveProgress> progress = new(item =>
            {
                // Bound pipe traffic so fast codecs cannot starve the UI with queued reports.
                lock (writeLock)
                {
                    long now = Environment.TickCount64;
                    if (now - lastReport < 100) return;
                    lastReport = now;
                    Send(new(Progress: item));
                }
            });
            WorkerMessage result = new(Completed: true);
            if (request.Operation == "create")
            {
                await CreateAsync(request, service, progress);
            }
            else
            {
                bool protectedEnvelope = PasswordArchiveService.IsProtected(request.Path);
                InlineProgress<ArchiveProgress> decryptProgress = new(item =>
                    progress.Report(new ArchiveProgress(item.Percentage / 5, item.EntryKey)));
                await using MaterializedArchive materialized = await PasswordArchiveService.OpenAsync(
                    request.Path, request.Password, decryptProgress, CancellationToken.None);
                InlineProgress<ArchiveProgress> archiveProgress = new(item =>
                    progress.Report(new ArchiveProgress((protectedEnvelope ? 20 : 0) +
                        item.Percentage * (protectedEnvelope ? 80 : 100) / 100, item.EntryKey)));
                switch (request.Operation)
                {
                    case "analyze":
                        result = result with { Nested = await service.AnalyzeNestedTarAsync(materialized.Path, CancellationToken.None, request.Password, archiveProgress) };
                        break;
                    case "list":
                        result = result with { Entries = (await service.ListAsync(materialized.Path, CancellationToken.None, request.Password)).ToArray() };
                        break;
                    case "list-tar":
                        result = result with { Entries = (await service.ListNestedTarAsync(materialized.Path, request.TarKeys![0], CancellationToken.None, request.Password)).ToArray() };
                        break;
                    case "extract":
                        await service.ExtractAsync(materialized.Path, request.Destination!, request.Selection, request.Policy, archiveProgress, CancellationToken.None, request.Password);
                        break;
                    case "extract-tar":
                        await service.ExtractNestedTarsAsync(materialized.Path, request.TarKeys!, request.Destination!, request.Selection, request.Subfolders, request.Policy, archiveProgress, CancellationToken.None, request.Password);
                        break;
                    default: throw new InvalidDataException();
                }
            }
            Send(result);
            return 0;
        }
        catch (Exception exception)
        {
            Send(new(StageCode: exception is StageException stage ? stage.StageCode : "WORKR0001", Error: exception.Message)); //WORKR0001
            return 1;
        }
    }

    private static async Task CreateAsync(WorkerRequest request, ArchiveService service, IProgress<ArchiveProgress> progress)
    {
        if (string.IsNullOrEmpty(request.Password))
        {
            if (request.Type == FileDetector.FileType.Rar)
                await new RarToolService().CreateAsync(request.Sources!, request.Path, progress, CancellationToken.None);
            else
                await service.CreateAsync(request.Sources!, request.Path, request.Type, progress, CancellationToken.None);
            return;
        }

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "TangerineZipPasswordCreate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string unprotectedArchive = Path.Combine(temporaryDirectory, Path.GetFileName(request.Path));
        try
        {
            InlineProgress<ArchiveProgress> createProgress = new(item =>
                progress.Report(new ArchiveProgress(item.Percentage * 80 / 100, item.EntryKey)));
            if (request.Type == FileDetector.FileType.Rar)
                await new RarToolService().CreateAsync(request.Sources!, unprotectedArchive, createProgress, CancellationToken.None);
            else
                await service.CreateAsync(request.Sources!, unprotectedArchive, request.Type, createProgress, CancellationToken.None);
            InlineProgress<ArchiveProgress> encryptionProgress = new(item =>
                progress.Report(new ArchiveProgress(80 + item.Percentage * 20 / 100, item.EntryKey)));
            await PasswordArchiveService.EncryptAsync(unprotectedArchive, request.Path, request.Type, request.Password,
                encryptionProgress, CancellationToken.None);
        }
        finally
        {
            try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); }
            catch (Exception exception) { throw new StageException("PWDAR0004", LanguageManager.Get("ArchivePasswordCleanupFailed"), exception); } //PWDAR0004
        }
    }
}

internal sealed class ArchiveWorkerClient
{
    private static async Task<WorkerMessage> RunAsync(WorkerRequest request, IProgress<ArchiveProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Use the current apphost path so renaming a published single-file executable remains supported.
        string executable = System.Reflection.Assembly.GetEntryAssembly() == typeof(ArchiveWorker).Assembly
            ? Environment.ProcessPath!
            : Path.Combine(AppContext.BaseDirectory, "TANGERINE-ZIP.exe");
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(ArchiveWorker.Switch);
        using Process process = new() { StartInfo = start };
        bool started = false;
        try
        {
            if (!process.Start()) throw new InvalidOperationException(LanguageManager.Get("WorkerFailed"));
            started = true;
            Task<string> errors = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request with { Culture = CultureInfo.CurrentUICulture.Name }));
            process.StandardInput.Close();
            WorkerMessage? result = null;
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                WorkerMessage message = JsonSerializer.Deserialize<WorkerMessage>(line) ?? throw new InvalidDataException();
                if (message.Progress is not null) progress?.Report(message.Progress);
                if (message.Completed || message.Error is not null) result = message;
            }
            await process.WaitForExitAsync(token);
            string standardError = await errors;
            if (result?.Error is not null) throw new StageException(result.StageCode!, result.Error);
            if (process.ExitCode != 0 || result?.Completed != true)
                throw new StageException("WORKR0002", LanguageManager.Get("WorkerFailed") + " " + standardError); //WORKR0002
            return result;
        }
        catch (OperationCanceledException)
        {
            // Kill the complete process tree, including rar.exe, and wait until file handles close.
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (Exception exception)
            {
                throw new StageException("WORKR0003", exception.Message, exception); //WORKR0003
            }
            throw;
        }
        catch (StageException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("WORKR0004", exception.Message, exception); //WORKR0004
        }
        finally
        {
            // A protocol/read failure must not leave an invisible writer running after the UI reports failure.
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                catch (Exception exception)
                {
                    throw new StageException("WORKR0005", exception.Message, exception); //WORKR0005
                }
            }
        }
    }

    public async Task<NestedTarInfo> AnalyzeNestedTarAsync(string path, CancellationToken token, string? password = null,
        IProgress<ArchiveProgress>? progress = null) =>
        (await RunAsync(new("analyze", path, Password: password), progress, token)).Nested!;
    public async Task<IReadOnlyList<ArchiveEntryInfo>> ListAsync(string path, CancellationToken token, string? password = null) =>
        (await RunAsync(new("list", path, Password: password), null, token)).Entries!;
    public async Task<IReadOnlyList<ArchiveEntryInfo>> ListNestedTarAsync(string path, string key, CancellationToken token, string? password = null) =>
        (await RunAsync(new("list-tar", path, TarKeys: [key], Password: password), null, token)).Entries!;
    public Task ExtractAsync(string path, string destination, IReadOnlyCollection<string>? selection, OverwritePolicy policy, IProgress<ArchiveProgress>? progress, CancellationToken token, string? password = null) =>
        RunAsync(new("extract", path, destination, Selection: selection?.ToArray(), Policy: policy, Password: password), progress, token);
    public Task ExtractNestedTarsAsync(string path, IReadOnlyList<string> keys, string destination, IReadOnlyCollection<string>? selection, bool subfolders, OverwritePolicy policy, IProgress<ArchiveProgress>? progress, CancellationToken token, string? password = null) =>
        RunAsync(new("extract-tar", path, destination, TarKeys: keys.ToArray(), Selection: selection?.ToArray(), Subfolders: subfolders, Policy: policy, Password: password), progress, token);
    public Task CreateAsync(IReadOnlyList<string> sources, string path, FileDetector.FileType type, IProgress<ArchiveProgress>? progress, CancellationToken token, string? password = null) =>
        RunAsync(new("create", path, Sources: sources.ToArray(), Type: type, Password: password), progress, token);
}
