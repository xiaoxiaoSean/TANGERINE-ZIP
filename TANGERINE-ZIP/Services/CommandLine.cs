using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Media;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// The GUI apphost uses the Windows subsystem. Attach to its parent console so the
// same executable can act as a command-line program without opening a second window.
internal static class CommandLine
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
        { "help", "compress", "extract", "list", "add", "batch-extract", "convert", "sfx", "test", "hash", "repair", "comment", "vault", "scan", "snapshot", "edit", "nested-tar", "temp", "integration", "appearance", "profile" };

    public static bool IsCommand(string[] args) => args.Length > 0 &&
        !args[0].StartsWith("--context-", StringComparison.Ordinal) &&
        (Commands.Contains(args[0]) || args[0] is "--help" or "-h" ||
         args[0].StartsWith("-", StringComparison.Ordinal) ||
         args.Length > 1 || !File.Exists(args[0]));

    public static async Task<int> RunAsync(string[] args)
    {
        // CLI output is deliberately stable English on every Windows locale.
        // The archive worker is a separate process, so the environment marker
        // carries this choice across its process boundary as well.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Environment.SetEnvironmentVariable("TZIP_CLI_ENGLISH", "1");
        // Attaching can replace the standard handles supplied by a script. Keep
        // redirected pipe/file handles intact; only attach for interactive output.
        // Failure simply means there is no caller console (e.g. Explorer launch).
        // Never fall back to AllocConsole, a terminal, cmd.exe, or PowerShell.
        if (!IsRedirected(GetStdHandle(-11)) && !IsRedirected(GetStdHandle(-12)))
            AttachConsole(unchecked((uint)-1));
        using StreamWriter standardOutput = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        using StreamWriter standardError = new(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(standardOutput);
        Console.SetError(standardError);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string command = args[0].ToLowerInvariant();
            if (command is "help" or "--help" or "-h")
            {
                PrintHelp(args.Length > 1 ? args[1] : null);
                return 0;
            }
            if (!Commands.Contains(command)) throw Usage(command);
            // The worker uses the configured app-owned temporary workspace. CLI calls
            // report missing or unreachable TEMP_D through stderr, without opening WPF.
            if (command is not ("hash" or "vault" or "scan" or "snapshot" or "temp" or "integration" or "appearance" or "profile"))
                TempDirectorySettings.Initialize();
            using CancellationTokenSource cancellation = new();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                switch (command)
                {
                    case "compress": await CompressAsync(args[1..], cancellation.Token); break;
                    case "extract": await ExtractAsync(args[1..], cancellation.Token); break;
                    case "list": await ListAsync(args[1..], cancellation.Token); break;
                    case "add": await AddAsync(args[1..], cancellation.Token); break;
                    case "batch-extract": await BatchExtractAsync(args[1..], cancellation.Token); break;
                    case "convert": await ConvertAsync(args[1..], cancellation.Token); break;
                    case "sfx": await SfxAsync(args[1..], cancellation.Token); break;
                    case "test": await TestAsync(args[1..], cancellation.Token); break;
                    case "hash": await HashAsync(args[1..], cancellation.Token); break;
                    case "repair": await RepairAsync(args[1..], cancellation.Token); break;
                    case "comment": await CommentAsync(args[1..], cancellation.Token); break;
                    case "vault": Vault(args[1..]); break;
                    case "scan": await ScanAsync(args[1..], cancellation.Token); break;
                    case "snapshot": await SnapshotAsync(args[1..], cancellation.Token); break;
                    case "edit": await EditAsync(args[1..], cancellation.Token); break;
                    case "nested-tar": await NestedTarAsync(args[1..], cancellation.Token); break;
                    case "temp": await TempAsync(args[1..]); break;
                    case "integration": await IntegrationAsync(args[1..], cancellation.Token); break;
                    case "appearance": await AppearanceAsync(args[1..]); break;
                    case "profile": await ProfileAsync(args[1..]); break;
                    default: throw Usage(command);
                }
                return 0;
            }
            finally { Console.CancelKeyPress -= cancelHandler; }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"[CLINE0015] {LanguageManager.Get("CliCancelled")}"); //CLINE0015
            return 2;
        }
        catch (Exception exception)
        {
            string stage = exception switch
            {
                StageException staged => staged.StageCode,
                ColorContrastException contrast => contrast.StageCode,
                _ => "CLINE0001"
            };
            // Runtime and third-party exception messages may be in the OS or
            // library language. Public CLI errors always use a translated
            // boundary message; StageException already carries one.
            string message = exception is StageException or ColorContrastException
                ? exception.Message : LanguageManager.Get("CliUnexpectedFailure");
            Console.Error.WriteLine($"[{stage}] {message}"); //CLINE0001
            if (stage.StartsWith("TMPDR", StringComparison.Ordinal))
                Console.Error.WriteLine(LanguageManager.Get("CliTempHint"));
            return 1;
        }
    }

    private static async Task CompressAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("compress", args, "password", "password-env", "format", "level", "method",
            "dictionary", "threads", "memory-limit", "volume", "solid", "recovery-percent", "exclude", "sfx",
            "iso-volume", "iso-manufacturer", "iso-joliet", "iso-deduplicate", "iso-boot-image",
            "iso-emulation", "iso-load-segment", "iso-isolinux", "profile");
        if (parsed.Positionals.Count < 2) throw Usage("compress");
        string output = Path.GetFullPath(parsed.Positionals[0]);
        string[] sources = parsed.Positionals.Skip(1).Select(Path.GetFullPath).ToArray();
        if (File.Exists(output) || Directory.Exists(output) || File.Exists(output + ".001"))
            throw new StageException("CLINE0002", LanguageManager.Get("CliOutputExists")); //CLINE0002
        foreach (string source in sources)
            if (!File.Exists(source) && !Directory.Exists(source))
                throw new FileNotFoundException(LanguageManager.Get("CliSourceMissing"), source);
        FileDetector.FileType format = GetFormat(parsed.Single("format"), output);
        if (!ArchiveCapabilities.CanCreate(format)) throw Usage("compress");
        string? password = GetPassword(parsed);
        bool advanced = parsed.HasAny("level", "method", "dictionary", "threads", "memory-limit", "volume",
            "solid", "recovery-percent", "exclude", "sfx");
        if (parsed.Has("profile") && advanced) throw Usage("compress");
        int level = parsed.Int("level", 5, 0, format == FileDetector.FileType.Rar ? 5 : 9);
        int dictionary = parsed.Int("dictionary", 16, 1, 1024);
        int threads = parsed.Int("threads", 0, 0, 128);
        int memory = parsed.Int("memory-limit", 0, 0, 1048576);
        int volume = parsed.Int("volume", 0, 0, 1048576);
        int recoveryPercent = parsed.Int("recovery-percent", 0, 0, 10);
        string solidMode = (parsed.Single("solid") ?? "default").ToLowerInvariant() switch
        {
            "default" => "Default", "on" => "On", "off" => "Off", _ => throw Usage("compress")
        };
        bool sfx = parsed.Has("sfx");
        string[] excludes = parsed.Many("exclude").ToArray();
        string method = (parsed.Single("method") ?? "Default").ToLowerInvariant() switch
        {
            "default" => "Default", "deflate" => "Deflate", "lzma2" => "LZMA2",
            _ => throw Usage("compress")
        };
        if (advanced && format is not (FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Rar))
            throw Usage("compress");
        if (advanced && (format == FileDetector.FileType.Zip && method == "LZMA2" ||
            format == FileDetector.FileType.SevenZip && method == "Deflate"))
            throw Usage("compress");
        if (parsed.Has("solid") && format is not (FileDetector.FileType.SevenZip or FileDetector.FileType.Rar) ||
            parsed.Has("recovery-percent") && format != FileDetector.FileType.Rar ||
            sfx && (format != FileDetector.FileType.SevenZip || volume > 0 ||
                !output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ||
            excludes.Any(pattern => string.IsNullOrWhiteSpace(pattern) || pattern.Length > 260 ||
                pattern.Contains('\r') || pattern.Contains('\n')))
            throw Usage("compress");
        if (password is not null && !ArchiveCapabilities.CanCreateWithPassword(format))
            throw new StageException("CLINE0003", LanguageManager.Get("PasswordFormatUnsupported")); //CLINE0003
        bool isoFlags = parsed.HasAny("iso-volume", "iso-manufacturer", "iso-joliet",
            "iso-deduplicate", "iso-boot-image", "iso-emulation", "iso-load-segment", "iso-isolinux");
        if (isoFlags && format != FileDetector.FileType.Iso) throw Usage("compress");
        IsoCreationOptions? iso = null;
        if (format == FileDetector.FileType.Iso)
        {
            string emulation = (parsed.Single("iso-emulation") ?? "none").ToLowerInvariant() switch
            {
                "none" => "NoEmulation", "floppy1200" => "Diskette1200KiB",
                "floppy1440" => "Diskette1440KiB", "floppy2880" => "Diskette2880KiB",
                "harddisk" => "HardDisk", _ => throw Usage("compress")
            };
            string? image = parsed.Single("iso-boot-image");
            iso = new IsoCreationOptions(parsed.Single("iso-volume") ?? "TANGERINE_ZIP",
                parsed.Single("iso-manufacturer") ?? string.Empty,
                ParseIsoToggle(parsed.Single("iso-joliet"), true),
                ParseIsoToggle(parsed.Single("iso-deduplicate"), false),
                image is not null, image is null ? null : Path.GetFullPath(image), emulation,
                parsed.Int("iso-load-segment", 0, 0, 65535),
                ParseIsoToggle(parsed.Single("iso-isolinux"), false));
            if (image is null && parsed.HasAny("iso-emulation", "iso-load-segment", "iso-isolinux"))
                throw Usage("compress");
        }
        CompressionOptions options = new(password, advanced, level, method, dictionary, threads, memory, volume,
            sfx, solidMode, recoveryPercent, excludes, iso);
        if (parsed.Single("profile") is string profileName)
        {
            if (isoFlags || format == FileDetector.FileType.Iso) throw Usage("compress");
            CompressionProfile profile = CompressionProfileStore.Load(format).FirstOrDefault(item =>
                item.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)) ??
                throw new StageException("CLINE0013", LanguageManager.Get("CliProfileMissing")); //CLINE0013
            // Saved profiles deliberately omit secrets; a password supplied to
            // this invocation remains the only credential sent to the worker.
            options = profile.Options with { Password = password };
        }
        await new ArchiveWorkerClient().CreateAsync(sources, output, format, null, token, password, options);
        Console.WriteLine(string.Format(LanguageManager.Get("CliCreated"), output));
    }

    private static async Task ExtractAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("extract", args, "password", "password-env", "encoding", "entry", "on-conflict", "on-issue");
        if (parsed.Positionals.Count != 2) throw Usage("extract");
        string archive = RequireArchive(parsed.Positionals[0]);
        string destination = Path.GetFullPath(parsed.Positionals[1]);
        string? password = GetPassword(parsed);
        OverwritePolicy policy = (parsed.Single("on-conflict") ?? "abort").ToLowerInvariant() switch
        {
            "overwrite" => OverwritePolicy.OverwriteAll,
            "skip" => OverwritePolicy.SkipAll,
            "abort" => OverwritePolicy.Ask,
            _ => throw Usage("extract")
        };
        string issuePolicy = (parsed.Single("on-issue") ?? "abort").ToLowerInvariant();
        if (issuePolicy is not ("abort" or "skip")) throw Usage("extract");
        ArchiveWorkerClient client = new() { EntryEncodingName = ValidateEncoding(parsed.Single("encoding")) };
        IReadOnlyList<ArchiveEntryInfo> entries = await client.ListAsync(archive, token, password);
        string[] selected = parsed.Many("entry").ToArray();
        if (selected.Length > 0 && selected.Any(key => !entries.Any(entry => entry.Key == key)))
            throw new StageException("CLINE0004", LanguageManager.Get("CliEntryMissing")); //CLINE0004
        ResourcePreflight.ForExtraction(archive, destination, selected.Length == 0
            ? entries : entries.Where(entry => selected.Any(key => entry.Key == key || entry.Key.StartsWith(key.TrimEnd('/') + "/", StringComparison.Ordinal))).ToArray());
        await client.ExtractAsync(archive, destination, selected.Length == 0 ? null : selected,
            policy, null, token, password,
            (conflict, _) => Task.FromResult(ConflictChoice.Cancel),
            (issue, _) => Task.FromResult(new ExtractionAnswer(issuePolicy == "skip"
                ? ExtractionDecision.Skip : ExtractionDecision.Stop)));
        Console.WriteLine(string.Format(LanguageManager.Get("CliExtracted"), destination));
    }

    private static async Task ListAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("list", args, "password", "password-env", "encoding", "json", "search");
        if (parsed.Positionals.Count != 1) throw Usage("list");
        ArchiveWorkerClient client = new() { EntryEncodingName = ValidateEncoding(parsed.Single("encoding")) };
        IReadOnlyList<ArchiveEntryInfo> entries = await client.ListAsync(RequireArchive(parsed.Positionals[0]),
            token, GetPassword(parsed));
        // Filtering the same member metadata as the GUI search keeps JSON and
        // tabular output consistent, including directories and unknown sizes.
        if (parsed.Single("search") is string query)
            entries = entries.Where(entry => entry.Key.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (parsed.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(entries));
            return;
        }
        Console.WriteLine(LanguageManager.Get("CliListHeader"));
        foreach (ArchiveEntryInfo entry in entries)
            Console.WriteLine($"{Escape(entry.Key)}\t{(entry.SizeKnown ? entry.Size.ToString(CultureInfo.InvariantCulture) : "-")}\t" +
                $"{entry.CompressedSize?.ToString(CultureInfo.InvariantCulture) ?? "-"}\t{entry.CompressionMethod ?? "-"}");
    }

    private static async Task AddAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("add", args);
        if (parsed.Positionals.Count < 2) throw Usage("add");
        string archive = RequireArchive(parsed.Positionals[0]);
        string[] sources = parsed.Positionals.Skip(1).Select(Path.GetFullPath).ToArray();
        string backup = await new ArchiveWorkerClient().AddFilesAsync(archive, sources, null, token);
        Console.WriteLine(string.Format(LanguageManager.Get("CliArchiveUpdated"), backup));
    }

    private static async Task BatchExtractAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("batch-extract", args, "password", "password-env");
        if (parsed.Positionals.Count < 2) throw Usage("batch-extract");
        string destination = Path.GetFullPath(parsed.Positionals[0]);
        string[] sources = parsed.Positionals.Skip(1).Select(RequireArchive).ToArray();
        await ArchiveBatchService.ExtractAsync(sources, destination, GetPassword(parsed),
            new ArchiveWorkerClient(), null, token,
            (_, _) => Task.FromResult(new ExtractionAnswer(ExtractionDecision.Stop)));
        Console.WriteLine(string.Format(LanguageManager.Get("CliBatchCompleted"), sources.Length));
    }

    private static async Task ConvertAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("convert", args, "format", "password", "password-env", "output-password-env");
        if (parsed.Positionals.Count < 2 || parsed.Single("format") is null) throw Usage("convert");
        string destination = Path.GetFullPath(parsed.Positionals[0]);
        string[] sources = parsed.Positionals.Skip(1).Select(RequireArchive).ToArray();
        FileDetector.FileType target = GetFormat(parsed.Single("format"), string.Empty);
        if (target is not (FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Tar))
            throw Usage("convert");
        string? outputPassword = parsed.Single("output-password-env") is string variable
            ? Environment.GetEnvironmentVariable(variable) : null;
        if (parsed.Has("output-password-env") && string.IsNullOrEmpty(outputPassword))
            throw new StageException("CLINE0006", LanguageManager.Get("CliPasswordEnvMissing")); //CLINE0006
        if (outputPassword is not null && (target == FileDetector.FileType.Tar ||
            outputPassword.Any(character => character is < ' ' or > '~')))
            throw Usage("convert");
        await ArchiveBatchService.ConvertAsync(sources, destination, target, GetPassword(parsed),
            outputPassword, new ArchiveWorkerClient(), null, token,
            (_, _) => Task.FromResult(new ExtractionAnswer(ExtractionDecision.Stop)));
        Console.WriteLine(string.Format(LanguageManager.Get("CliBatchCompleted"), sources.Length));
    }

    private static async Task TestAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("test", args, "password", "password-env");
        if (parsed.Positionals.Count != 1) throw Usage("test");
        // A damaged member is a failed command even when the archive directory
        // itself could be opened. Preserve the per-member report for scripts.
        IReadOnlyList<EntryTestResult> entries = await ArchiveDiagnostics.ScanEntriesAsync(
            RequireArchive(parsed.Positionals[0]), GetPassword(parsed), token);
        int failed = entries.Count(entry => !entry.Healthy);
        Console.WriteLine(string.Format(LanguageManager.Get("IntegrityCrcResult"), entries.Count, failed));
        foreach (EntryTestResult entry in entries.Where(entry => !entry.Healthy))
            Console.WriteLine(entry.Name + ": " + entry.Message);
        if (failed != 0)
            throw new StageException("CLINE0011", LanguageManager.Get("CliIntegrityFailed")); //CLINE0011
    }

    private static async Task SfxAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("sfx", args, "password", "password-env");
        if (parsed.Positionals.Count != 2) throw Usage("sfx");
        string output = Path.GetFullPath(parsed.Positionals[1]);
        await ArchiveSfxService.CreateAsync(parsed.Positionals[0], output, GetPassword(parsed),
            new ArchiveWorkerClient(), null, token);
        Console.WriteLine(string.Format(LanguageManager.Get("SfxCreated"), output));
    }

    private static async Task HashAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("hash", args, "algorithm", "expected");
        if (parsed.Positionals.Count != 1 || !File.Exists(parsed.Positionals[0])) throw Usage("hash");
        string algorithm = (parsed.Single("algorithm") ?? "SHA256").ToUpperInvariant();
        if (algorithm is not ("SHA256" or "SHA512" or "MD5")) throw Usage("hash");
        string actual = await ArchiveDiagnostics.ComputeHashAsync(Path.GetFullPath(parsed.Positionals[0]), algorithm, token);
        Console.WriteLine(actual);
        if (parsed.Single("expected") is string expected &&
            !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new StageException("CLINE0009", LanguageManager.Get("CliHashMismatch")); //CLINE0009
    }

    private static async Task RepairAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("repair", args, "password", "password-env");
        if (parsed.Positionals.Count != 1) throw Usage("repair");
        Console.WriteLine(await ArchiveDiagnostics.RecoverAsync(RequireArchive(parsed.Positionals[0]),
            GetPassword(parsed), token));
    }

    private static async Task CommentAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("comment", args, "set", "file");
        if (parsed.Positionals.Count != 1 || parsed.Has("set") && parsed.Has("file"))
            throw Usage("comment");
        string archive = RequireArchive(parsed.Positionals[0]);
        ArchiveWorkerClient worker = new();
        if (!parsed.Has("set") && !parsed.Has("file"))
        {
            Console.WriteLine(await worker.ReadCommentAsync(archive, token));
            return;
        }
        string comment = parsed.Single("file") is string path
            ? await File.ReadAllTextAsync(Path.GetFullPath(path), token)
            : parsed.Single("set")!;
        string backup = await worker.WriteCommentAsync(archive, comment, token);
        Console.WriteLine(string.Format(LanguageManager.Get("CliArchiveUpdated"), backup));
    }

    private static void Vault(string[] args)
    {
        Parsed parsed = Parse("vault", args, "password-env");
        if (parsed.Positionals.Count == 0) throw Usage("vault");
        string action = parsed.Positionals[0].ToLowerInvariant();
        if (action == "list" && parsed.Positionals.Count == 1 && !parsed.Has("password-env"))
        {
            foreach (string savedName in PasswordVaultService.ListNames()) Console.WriteLine(savedName);
            return;
        }
        if (parsed.Positionals.Count != 2) throw Usage("vault");
        string name = parsed.Positionals[1];
        if (action == "delete" && !parsed.Has("password-env"))
        {
            PasswordVaultService.Delete(name);
            Console.WriteLine(LanguageManager.Get("CliVaultDeleted"));
            return;
        }
        if (action is not ("save" or "check") || !parsed.Has("password-env")) throw Usage("vault");
        string password = GetPassword(parsed)!;
        if (action == "save")
        {
            PasswordVaultService.Save(name, password);
            Console.WriteLine(LanguageManager.Get("CliVaultSaved"));
            return;
        }
        byte[] expected = Encoding.UTF8.GetBytes(password);
        byte[] actual = Encoding.UTF8.GetBytes(PasswordVaultService.Read(name));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                throw new StageException("CLINE0010", LanguageManager.Get("CliVaultMismatch")); //CLINE0010
            Console.WriteLine(LanguageManager.Get("CliVaultMatched"));
        }
        finally { Array.Clear(expected); Array.Clear(actual); }
    }

    private static string RequireArchive(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException(LanguageManager.Get("CliArchiveMissing"), full);
        if (!ArchiveCapabilities.CanOpen(FileDetector.DetectFileType(full)))
            throw new StageException("CLINE0005", LanguageManager.Get("CliUnsupportedArchive")); //CLINE0005
        return full;
    }

    private static async Task ScanAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("scan", args);
        if (parsed.Positionals.Count != 1) throw Usage("scan");
        await DefenderScanService.ScanAsync(parsed.Positionals[0], token);
        Console.WriteLine(LanguageManager.Get("DefenderScanComplete"));
    }

    private static async Task SnapshotAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("snapshot", args, "keep");
        if (parsed.Positionals.Count != 2) throw Usage("snapshot");
        int keep = parsed.Int("keep", 0, 0, 1000);
        string output = await ArchiveSnapshotService.CreateAsync(parsed.Positionals[0],
            parsed.Positionals[1], keep, token);
        Console.WriteLine(string.Format(LanguageManager.Get("SnapshotCreated"), output));
    }

    /// <summary>
    /// Executes the same archive rewrite used by the GUI clipboard and Delete menu.
    /// Requiring explicit member keys and a destination prefix prevents an
    /// unattended invocation from editing an unintended directory or archive.
    /// The worker keeps the original archive as a sibling backup on success.
    /// </summary>
    private static async Task EditAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("edit", args, "entry", "destination", "encoding");
        if (parsed.Positionals.Count != 2) throw Usage("edit");
        string archive = RequireArchive(parsed.Positionals[0]);
        ArchiveEditAction action = parsed.Positionals[1].ToLowerInvariant() switch
        {
            "copy" => ArchiveEditAction.Copy,
            "move" => ArchiveEditAction.Move,
            "delete" => ArchiveEditAction.Delete,
            _ => throw Usage("edit")
        };
        string[] selected = parsed.Many("entry").ToArray();
        if (selected.Length == 0 || action == ArchiveEditAction.Delete && parsed.Has("destination") ||
            action != ArchiveEditAction.Delete && !parsed.Has("destination")) throw Usage("edit");
        string? encoding = ValidateEncoding(parsed.Single("encoding"));
        ArchiveWorkerClient client = new() { EntryEncodingName = encoding };
        IReadOnlyList<ArchiveEntryInfo> entries = await client.ListAsync(archive, token);
        if (selected.Any(key => !entries.Any(entry =>
            entry.Key.TrimEnd('/').Equals(key.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
            entry.Key.StartsWith(key.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))))
            throw new StageException("CLINE0004", LanguageManager.Get("CliEntryMissing")); //CLINE0004
        // The GUI passes a directory key ending in '/'. Normalize the shell
        // argument to that contract so `--destination copied` creates
        // copied/member.txt instead of a renamed root member.
        string destination = (parsed.Single("destination") ?? string.Empty).Replace('\\', '/');
        if (destination.Length > 0 && !destination.EndsWith('/')) destination += '/';
        string backup = await client.EditAsync(archive, selected,
            destination, action, null, token);
        Console.WriteLine(string.Format(LanguageManager.Get("CliArchiveUpdated"), backup));
    }

    /// <summary>
    /// Uses the archive worker's nested TAR detection and extraction path. A
    /// specified outer TAR member must be present in the analyzed archive; when
    /// omitted all detected TAR members are expanded into destination folders.
    /// Noninteractive conflicts and safety issues fail closed by default.
    /// </summary>
    private static async Task NestedTarAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("nested-tar", args, "entry", "password", "password-env",
            "on-conflict", "on-issue");
        if (parsed.Positionals.Count != 2) throw Usage("nested-tar");
        string archive = RequireArchive(parsed.Positionals[0]);
        string destination = Path.GetFullPath(parsed.Positionals[1]);
        string? password = GetPassword(parsed);
        OverwritePolicy policy = (parsed.Single("on-conflict") ?? "abort").ToLowerInvariant() switch
        {
            "abort" => OverwritePolicy.Ask,
            "overwrite" => OverwritePolicy.OverwriteAll,
            "skip" => OverwritePolicy.SkipAll,
            _ => throw Usage("nested-tar")
        };
        string issue = (parsed.Single("on-issue") ?? "abort").ToLowerInvariant();
        if (issue is not ("abort" or "skip")) throw Usage("nested-tar");
        ArchiveWorkerClient client = new();
        NestedTarInfo nested = await client.AnalyzeNestedTarAsync(archive, token, password);
        string[] requested = parsed.Many("entry").ToArray();
        if (requested.Any(key => !nested.TarEntryKeys.Contains(key, StringComparer.Ordinal)) ||
            nested.TarEntryKeys.Count == 0)
            throw new StageException("CLINE0012", LanguageManager.Get("CliNestedTarMissing")); //CLINE0012
        IReadOnlyList<string> selected = requested.Length == 0 ? nested.TarEntryKeys : requested;
        await client.ExtractNestedTarsAsync(archive, selected, destination, null, true,
            policy, null, token, password,
            (_, _) => Task.FromResult(ConflictChoice.Cancel),
            (_, _) => Task.FromResult(new ExtractionAnswer(issue == "skip"
                ? ExtractionDecision.Skip : ExtractionDecision.Stop)));
        Console.WriteLine(string.Format(LanguageManager.Get("CliExtracted"), destination));
    }

    /// <summary>
    /// Reads or changes the persisted TEMP_D selection without starting WPF.
    /// The setter performs the same validation and atomic save as Settings.
    /// </summary>
    private static async Task TempAsync(string[] args)
    {
        Parsed parsed = Parse("temp", args);
        if (parsed.Positionals.Count == 1 && parsed.Positionals[0].Equals("get", StringComparison.OrdinalIgnoreCase))
        {
            TempDirectorySettings.Initialize();
            Console.WriteLine(TempDirectorySettings.CurrentPath);
            return;
        }
        if (parsed.Positionals.Count == 2 && parsed.Positionals[0].Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            await TempDirectorySettings.SetAsync(parsed.Positionals[1]);
            Console.WriteLine(TempDirectorySettings.CurrentPath);
            return;
        }
        throw Usage("temp");
    }

    /// <summary>
    /// Exposes the GUI's Explorer and default-app operations through their
    /// existing services. Explicit subcommands and extension arguments keep
    /// registry changes reviewable in shell history; no broad change is made
    /// merely by invoking the command without an action.
    /// </summary>
    private static async Task IntegrationAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse("integration", args);
        if (parsed.Positionals.Count == 0) throw Usage("integration");
        string action = parsed.Positionals[0].ToLowerInvariant();
        if (parsed.Positionals.Count == 2 && action == "default")
        {
            string extension = parsed.Positionals[1].StartsWith('.')
                ? parsed.Positionals[1].ToLowerInvariant() : "." + parsed.Positionals[1].ToLowerInvariant();
            string executable = DefaultAppAssociationService.GetExecutablePath();
            await Task.Run(() => DefaultAppAssociationService.SetThisAppDefault(extension, executable), token);
            Console.WriteLine(string.Format(LanguageManager.Get("DefaultAppsVerified"), extension));
            return;
        }
        if (parsed.Positionals.Count == 2 && action == "choose-default")
        {
            string extension = parsed.Positionals[1].StartsWith('.')
                ? parsed.Positionals[1].ToLowerInvariant() : "." + parsed.Positionals[1].ToLowerInvariant();
            string executable = DefaultAppAssociationService.GetExecutablePath();
            // Match the GUI's explicit Windows Settings handoff: register this
            // app as an available choice, then let the user select any app.
            await Task.Run(() => DefaultAppAssociationService.RegisterHandler(extension, executable), token);
            DefaultSettingsLaunch launch = await Task.Run(() =>
                DefaultAppAssociationService.OpenWindowsSettings(useThisApp: false), token);
            foreach (StageException warning in launch.Warnings)
                Console.Error.WriteLine($"[{warning.StageCode}] {warning.Message}");
            Console.WriteLine(launch.Uri);
            return;
        }
        if (parsed.Positionals.Count != 1) throw Usage("integration");
        switch (action)
        {
            case "unregister-defaults":
                int removed = await Task.Run(() => DefaultAppUnregistrationService.Unregister(_ => { }), token);
                Console.WriteLine(string.Format(LanguageManager.Get("DefaultAppsUnregisterCompleted"), removed));
                break;
            case "create-context-menu":
                // Package extraction uses the app-owned temporary workspace.
                // Other integration actions do not stage package files.
                TempDirectorySettings.Initialize();
                ContextMenuCreationResult created = await ContextMenuRegistrationService.CreateAsync(
                    DefaultAppAssociationService.GetExecutablePath(), new QuietProgress<ContextMenuProgress>(), token);
                Console.WriteLine(LanguageManager.Get(created == ContextMenuCreationResult.ModernAndClassic
                    ? "ContextMenuBothCreated" : "ContextMenuClassicOnlyCreated"));
                break;
            case "delete-context-menu":
                await ContextMenuRegistrationService.DeleteAsync(
                    DefaultAppAssociationService.GetExecutablePath(), new QuietProgress<ContextMenuProgress>(), token);
                Console.WriteLine(LanguageManager.Get("ContextMenuDeleted"));
                break;
            default: throw Usage("integration");
        }
    }

    // The services require a progress sink, while scripts receive only a
    // deterministic result line and a StageCode on failure.
    private sealed class QuietProgress<T> : IProgress<T>
    {
        public void Report(T value) { }
    }

    /// <summary>
    /// Uses the same validated color and mouse-effect settings as the GUI.
    /// Color indexes are one-based to match COLOR1..COLOR5 configuration files.
    /// A failed settings load remains a diagnostic rather than silently
    /// replacing a user's configuration with default values.
    /// </summary>
    private static async Task AppearanceAsync(string[] args)
    {
        Parsed parsed = Parse("appearance", args);
        if (parsed.Positionals.Count == 0) throw Usage("appearance");
        string area = parsed.Positionals[0].ToLowerInvariant();
        if (area == "mouse")
        {
            try
            {
                MouseEffectSettings.Initialize();
                MouseEffectSettings.InitializeParameters();
            }
            catch (InvalidMouseEffectConfigurationException invalid)
            {
                throw new StageException(invalid.StageCode,
                    LanguageManager.Get("CliAppearanceInvalidConfig"), invalid);
            }
            if (parsed.Positionals.Count == 2 && parsed.Positionals[1].Equals("get", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"enabled={MouseEffectSettings.IsEnabled.ToString().ToLowerInvariant()} radius={MouseEffectSettings.Radius.ToString(CultureInfo.InvariantCulture)} thickness={MouseEffectSettings.Thickness.ToString(CultureInfo.InvariantCulture)}");
                return;
            }
            if (parsed.Positionals.Count != 3) throw Usage("appearance");
            string property = parsed.Positionals[1].ToLowerInvariant();
            string value = parsed.Positionals[2];
            if (property == "enabled" && value is "on" or "off")
                await MouseEffectSettings.SetEnabledAsync(value == "on");
            else if (property is "radius" or "thickness" &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                if (property == "radius") await MouseEffectSettings.SetRadiusAsync(number);
                else await MouseEffectSettings.SetThicknessAsync(number);
            }
            else throw Usage("appearance");
            Console.WriteLine(LanguageManager.Get("CliSettingSaved"));
            return;
        }
        if (area == "color")
        {
            try { await AppearanceSettings.InitializeAsync(); }
            catch (InvalidColorConfigurationException invalid)
            {
                throw new StageException(invalid.StageCode,
                    LanguageManager.Get("CliAppearanceInvalidConfig"), invalid);
            }
            catch (ColorContrastException contrast)
            {
                throw new StageException(contrast.StageCode, contrast.Message, contrast);
            }
            if (parsed.Positionals.Count < 2 || !int.TryParse(parsed.Positionals[1],
                NumberStyles.None, CultureInfo.InvariantCulture, out int fileNumber) || fileNumber is < 1 or > 5)
                throw Usage("appearance");
            int index = fileNumber - 1;
            if (parsed.Positionals.Count == 3 && parsed.Positionals[2].Equals("get", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(AppearanceSettings.Format(AppearanceSettings.GetColor(index)));
                return;
            }
            if (parsed.Positionals.Count == 3 && parsed.Positionals[2].Equals("reset-pair", StringComparison.OrdinalIgnoreCase) && index is 0 or 2)
                await AppearanceSettings.ResetPairAsync(index);
            else if (parsed.Positionals.Count == 3 && parsed.Positionals[2].Equals("reset", StringComparison.OrdinalIgnoreCase))
                await AppearanceSettings.SetColorAsync(index, AppearanceSettings.GetDefaultColor(index));
            else if (parsed.Positionals.Count == 4 && parsed.Positionals[2].Equals("set", StringComparison.OrdinalIgnoreCase) &&
                AppearanceSettings.TryParse(parsed.Positionals[3], out Color color))
                await AppearanceSettings.SetColorAsync(index, color);
            else throw Usage("appearance");
            Console.WriteLine(LanguageManager.Get("CliSettingSaved"));
            return;
        }
        throw Usage("appearance");
    }

    /// <summary>
    /// Manages the GUI's persisted compression profiles without duplicating
    /// its validation or write path. The input JSON is a CompressionOptions
    /// object; passwords are rejected by the shared profile store so a profile
    /// file can never become an accidental credential store.
    /// </summary>
    private static async Task ProfileAsync(string[] args)
    {
        Parsed parsed = Parse("profile", args);
        if (parsed.Positionals.Count < 2) throw Usage("profile");
        string action = parsed.Positionals[0].ToLowerInvariant();
        FileDetector.FileType format = GetFormat(parsed.Positionals[1], string.Empty);
        if (format is not (FileDetector.FileType.Zip or FileDetector.FileType.SevenZip or FileDetector.FileType.Rar))
            throw Usage("profile");
        if (action == "list" && parsed.Positionals.Count == 2)
        {
            foreach (CompressionProfile profile in CompressionProfileStore.Load(format))
                Console.WriteLine(profile.Name);
            return;
        }
        if (parsed.Positionals.Count < 3) throw Usage("profile");
        string name = parsed.Positionals[2];
        if (action == "show" && parsed.Positionals.Count == 3)
        {
            CompressionProfile profile = FindProfile(format, name);
            Console.WriteLine(JsonSerializer.Serialize(profile.Options, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (action == "delete" && parsed.Positionals.Count == 3)
        {
            _ = FindProfile(format, name);
            await CompressionProfileStore.DeleteAsync(format, name);
            Console.WriteLine(LanguageManager.Get("CliProfileDeleted"));
            return;
        }
        if (action == "save" && parsed.Positionals.Count == 4)
        {
            CompressionOptions? options;
            try
            {
                string json = await File.ReadAllTextAsync(Path.GetFullPath(parsed.Positionals[3]));
                options = JsonSerializer.Deserialize<CompressionOptions>(json);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new StageException("CLINE0014", LanguageManager.Get("CliProfileInvalidFile"), error); //CLINE0014
            }
            if (options is null)
                throw new StageException("CLINE0014", LanguageManager.Get("CliProfileInvalidFile")); //CLINE0014
            await CompressionProfileStore.SaveAsync(new CompressionProfile(name, format, options));
            Console.WriteLine(LanguageManager.Get("CliSettingSaved"));
            return;
        }
        throw Usage("profile");
    }

    private static CompressionProfile FindProfile(FileDetector.FileType format, string name) =>
        CompressionProfileStore.Load(format).FirstOrDefault(profile =>
            profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ??
        throw new StageException("CLINE0013", LanguageManager.Get("CliProfileMissing")); //CLINE0013

    private static string? ValidateEncoding(string? name)
    {
        if (name is not null) _ = Encoding.GetEncoding(name);
        return name;
    }

    private static string? GetPassword(Parsed parsed)
    {
        string? direct = parsed.Single("password");
        string? variable = parsed.Single("password-env");
        if (direct is not null && variable is not null) throw Usage(parsed.Command);
        string? password = variable is null ? direct : Environment.GetEnvironmentVariable(variable);
        if (variable is not null && string.IsNullOrEmpty(password))
            throw new StageException("CLINE0006", LanguageManager.Get("CliPasswordEnvMissing")); //CLINE0006
        if (password is not null && password.Any(character => character is < ' ' or > '~'))
            throw new StageException("CLINE0008", LanguageManager.Get("PasswordAsciiOnly")); //CLINE0008
        return password;
    }

    private static FileDetector.FileType GetFormat(string? requested, string output)
    {
        string value = (requested ?? Path.GetExtension(output).TrimStart('.')).ToLowerInvariant();
        return value switch
        {
            "zip" => FileDetector.FileType.Zip, "7z" => FileDetector.FileType.SevenZip,
            "rar" => FileDetector.FileType.Rar, "tar" => FileDetector.FileType.Tar,
            "gz" or "gzip" => FileDetector.FileType.GZip,
            "bz2" or "bzip2" => FileDetector.FileType.BZip2,
            "xz" => FileDetector.FileType.Xz, "lz4" => FileDetector.FileType.Lz4,
            "zst" or "zstd" => FileDetector.FileType.Zstd,
            "lz" or "lzip" => FileDetector.FileType.Lzip,
            "arj" => FileDetector.FileType.Arj, "ace" => FileDetector.FileType.Ace,
            "arc" => FileDetector.FileType.Arc,
            "z" or "lzw" => FileDetector.FileType.Lzw,
            "iso" => FileDetector.FileType.Iso, "wim" => FileDetector.FileType.Wim,
            _ => FileDetector.FileType.Unknown
        };
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t")
        .Replace("\r", "\\r").Replace("\n", "\\n");

    private static bool ParseIsoToggle(string? value, bool fallback) => value?.ToLowerInvariant() switch
    {
        null => fallback, "on" => true, "off" => false, _ => throw Usage("compress")
    };

    private static StageException Usage(string command) => new("CLINE0007",
        LanguageManager.Get("CliInvalidArguments") + Environment.NewLine +
        LanguageManager.Get(HelpKey(command))); //CLINE0007

    private static string HelpKey(string? command) => command?.ToLowerInvariant() switch
    {
        "compress" => "CliHelp_compressV2", "extract" => "CliHelp_extract",
        "list" => "CliHelp_list", "add" => "CliHelp_add",
        "batch-extract" => "CliHelp_batch-extract", "convert" => "CliHelp_convert",
        "sfx" => "CliHelp_sfx",
        "test" => "CliHelp_test", "hash" => "CliHelp_hash",
        "repair" => "CliHelp_repair", "comment" => "CliHelp_comment",
        "vault" => "CliHelp_vault", "scan" => "CliHelp_scan",
        "snapshot" => "CliHelp_snapshot", "edit" => "CliHelp_edit",
        "nested-tar" => "CliHelp_nested-tar", "temp" => "CliHelp_temp",
        "integration" => "CliHelp_integration", "appearance" => "CliHelp_appearance",
        "profile" => "CliHelp_profile",
        _ => "CliHelpV3"
    };

    private static void PrintHelp(string? command)
    {
        Console.WriteLine(LanguageManager.Get(HelpKey(command)));
    }

    private static Parsed Parse(string command, string[] args, params string[] allowed)
    {
        Parsed parsed = new(command);
        HashSet<string> valid = new(allowed, StringComparer.Ordinal);
        bool positionalOnly = false;
        for (int index = 0; index < args.Length; index++)
        {
            string token = args[index];
            if (!positionalOnly && token == "--") { positionalOnly = true; continue; }
            if (!positionalOnly && token.StartsWith("--", StringComparison.Ordinal))
            {
                string name = token[2..];
                if (!valid.Contains(name)) throw Usage(parsed.Command);
                string value = name is "json" or "sfx" ? "true" :
                    ++index < args.Length ? args[index] : throw Usage(parsed.Command);
                if (name is not ("entry" or "exclude") && parsed.Has(name)) throw Usage(parsed.Command);
                parsed.Add(name, value);
            }
            else parsed.Positionals.Add(token);
        }
        return parsed;
    }

    private sealed class Parsed(string command)
    {
        private readonly Dictionary<string, List<string>> _options = new(StringComparer.Ordinal);
        public string Command { get; } = command;
        public List<string> Positionals { get; } = [];
        public void Add(string name, string value)
        {
            if (!_options.TryGetValue(name, out List<string>? values)) _options[name] = values = [];
            values.Add(value);
        }
        public bool Has(string name) => _options.ContainsKey(name);
        public bool HasAny(params string[] names) => names.Any(Has);
        public string? Single(string name) => _options.TryGetValue(name, out List<string>? values) ? values[0] : null;
        public IEnumerable<string> Many(string name) => _options.TryGetValue(name, out List<string>? values) ? values : [];
        public int Int(string name, int fallback, int min, int max)
        {
            string? value = Single(name);
            if (value is null) return fallback;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < min || number > max)
                throw Usage(Command);
            return number;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    private static bool IsRedirected(IntPtr handle) =>
        handle != IntPtr.Zero && handle != new IntPtr(-1) && GetFileType(handle) is 1 or 3; // FILE_TYPE_DISK / PIPE

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);
}
