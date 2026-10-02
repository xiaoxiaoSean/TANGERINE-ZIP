using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// The GUI apphost uses the Windows subsystem. Attach to its parent console so the
// same executable can act as a command-line program without opening a second window.
internal static class CommandLine
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
        { "help", "compress", "extract", "list", "add", "batch-extract", "convert", "sfx", "test", "hash", "repair", "comment", "vault", "scan", "snapshot" };

    public static bool IsCommand(string[] args) => args.Length > 0 &&
        !args[0].StartsWith("--context-", StringComparison.Ordinal) &&
        (Commands.Contains(args[0]) || args[0] is "--help" or "-h" ||
         args[0].StartsWith("-", StringComparison.Ordinal) ||
         args.Length > 1 || !File.Exists(args[0]));

    public static async Task<int> RunAsync(string[] args)
    {
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
            if (command is not ("hash" or "vault" or "scan" or "snapshot"))
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
                    default: throw Usage(command);
                }
                return 0;
            }
            finally { Console.CancelKeyPress -= cancelHandler; }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(LanguageManager.Get("CliCancelled"));
            return 2;
        }
        catch (Exception exception)
        {
            string stage = exception is StageException staged ? staged.StageCode : "CLINE0001";
            // Runtime and third-party exception messages may be in the OS or
            // library language. Public CLI errors always use a translated
            // boundary message; StageException already carries one.
            string message = exception is StageException
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
            "iso-emulation", "iso-load-segment", "iso-isolinux");
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
        Parsed parsed = Parse("list", args, "password", "password-env", "encoding", "json");
        if (parsed.Positionals.Count != 1) throw Usage("list");
        ArchiveWorkerClient client = new() { EntryEncodingName = ValidateEncoding(parsed.Single("encoding")) };
        IReadOnlyList<ArchiveEntryInfo> entries = await client.ListAsync(RequireArchive(parsed.Positionals[0]),
            token, GetPassword(parsed));
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
        "snapshot" => "CliHelp_snapshot", _ => "CliHelpV2"
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
