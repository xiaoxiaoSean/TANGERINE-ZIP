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
    NestedTarInfo? Nested = null, bool Completed = false, string? StageCode = null, string? Error = null,
    string? ConflictPath = null, string? ConflictEntry = null, bool Cancelled = false);

internal sealed record WorkerCommand(ConflictChoice ConflictChoice);

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
            ConflictResolutionState conflicts = new(conflict =>
            {
                Send(new(ConflictPath: conflict.TargetPath, ConflictEntry: conflict.EntryKey));
                string response = input.ReadLine() ?? throw new OperationCanceledException();
                WorkerCommand command = JsonSerializer.Deserialize<WorkerCommand>(response)
                    ?? throw new StageException("WORKR0007", LanguageManager.Get("InvalidConflictChoice")); //WORKR0007
                return command.ConflictChoice;
            });
            if (request.Operation == "create")
            {
                await CreateAsync(request, service, progress);
            }
            else
            {
                switch (request.Operation)
                {
                    case "analyze":
                        result = result with { Nested = await service.AnalyzeNestedTarAsync(request.Path, CancellationToken.None, request.Password, progress) };
                        break;
                    case "list":
                        result = result with { Entries = (await service.ListAsync(request.Path, CancellationToken.None, request.Password)).ToArray() };
                        break;
                    case "list-tar":
                        result = result with { Entries = (await service.ListNestedTarAsync(request.Path, request.TarKeys![0], CancellationToken.None, request.Password)).ToArray() };
                        break;
                    case "extract":
                        await service.ExtractAsync(request.Path, request.Destination!, request.Selection, request.Policy, progress, CancellationToken.None, request.Password, conflicts);
                        break;
                    case "extract-tar":
                        await service.ExtractNestedTarsAsync(request.Path, request.TarKeys!, request.Destination!, request.Selection, request.Subfolders, request.Policy, progress, CancellationToken.None, request.Password, conflicts);
                        break;
                    default: throw new InvalidDataException();
                }
            }
            Send(result);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Send(new(Cancelled: true));
            return 2;
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
        if (!ArchiveCapabilities.CanCreateWithPassword(request.Type))
            throw new StageException("WORKR0006", LanguageManager.Get("PasswordFormatUnsupported")); //WORKR0006
        if (request.Type == FileDetector.FileType.Rar)
            await new RarToolService().CreateAsync(request.Sources!, request.Path, progress, CancellationToken.None, request.Password);
        else if (request.Type == FileDetector.FileType.Zip)
            await new EncryptedZipService().CreateAsync(request.Sources!, request.Path, request.Password,
                progress, CancellationToken.None);
        else
            await new SevenZipToolService().CreateEncryptedAsync(request.Sources!, request.Path, request.Type,
                request.Password, progress, CancellationToken.None);
    }
}

internal sealed class ArchiveWorkerClient
{
    private static async Task<WorkerMessage> RunAsync(WorkerRequest request, IProgress<ArchiveProgress>? progress,
        CancellationToken token, Func<ArchiveConflict, CancellationToken, Task<ConflictChoice>>? conflictResolver = null)
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
            await process.StandardInput.FlushAsync(token);
            WorkerMessage? result = null;
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                WorkerMessage message = JsonSerializer.Deserialize<WorkerMessage>(line) ?? throw new InvalidDataException();
                if (message.Progress is not null) progress?.Report(message.Progress);
                if (message.ConflictPath is not null)
                {
                    ConflictChoice choice = conflictResolver is null ? ConflictChoice.Cancel :
                        await conflictResolver(new ArchiveConflict(message.ConflictPath,
                            message.ConflictEntry ?? string.Empty), token);
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new WorkerCommand(choice)));
                    await process.StandardInput.FlushAsync(token);
                }
                if (message.Completed || message.Error is not null || message.Cancelled) result = message;
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            string standardError = await errors;
            if (result?.Cancelled == true) throw new OperationCanceledException(token);
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
    public Task ExtractAsync(string path, string destination, IReadOnlyCollection<string>? selection, OverwritePolicy policy, IProgress<ArchiveProgress>? progress, CancellationToken token, string? password = null, Func<ArchiveConflict, CancellationToken, Task<ConflictChoice>>? conflictResolver = null) =>
        RunAsync(new("extract", path, destination, Selection: selection?.ToArray(), Policy: policy, Password: password), progress, token, conflictResolver);
    public Task ExtractNestedTarsAsync(string path, IReadOnlyList<string> keys, string destination, IReadOnlyCollection<string>? selection, bool subfolders, OverwritePolicy policy, IProgress<ArchiveProgress>? progress, CancellationToken token, string? password = null, Func<ArchiveConflict, CancellationToken, Task<ConflictChoice>>? conflictResolver = null) =>
        RunAsync(new("extract-tar", path, destination, TarKeys: keys.ToArray(), Selection: selection?.ToArray(), Subfolders: subfolders, Policy: policy, Password: password), progress, token, conflictResolver);
    public Task CreateAsync(IReadOnlyList<string> sources, string path, FileDetector.FileType type, IProgress<ArchiveProgress>? progress, CancellationToken token, string? password = null) =>
        RunAsync(new("create", path, Sources: sources.ToArray(), Type: type, Password: password), progress, token);
}
