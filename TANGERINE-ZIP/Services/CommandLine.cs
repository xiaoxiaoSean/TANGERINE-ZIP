using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// The GUI apphost uses the Windows subsystem. Attach to its parent console so the
// same executable can act as a command-line program without opening a second window.
internal static class CommandLine
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
        { "help", "compress", "extract", "list" };

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
            Console.Error.WriteLine($"[{stage}] {exception.Message}"); //CLINE0001
            if (stage.StartsWith("TMPDR", StringComparison.Ordinal))
                Console.Error.WriteLine(LanguageManager.Get("CliTempHint"));
            return 1;
        }
    }

    private static async Task CompressAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse(args, "password", "password-env", "format", "level", "method",
            "dictionary", "threads", "memory-limit", "volume");
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
        bool advanced = parsed.HasAny("level", "method", "dictionary", "threads", "memory-limit", "volume");
        int level = parsed.Int("level", 5, 0, format == FileDetector.FileType.Rar ? 5 : 9);
        int dictionary = parsed.Int("dictionary", 16, 1, 1024);
        int threads = parsed.Int("threads", 0, 0, 128);
        int memory = parsed.Int("memory-limit", 0, 0, 1048576);
        int volume = parsed.Int("volume", 0, 0, 1048576);
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
        if (password is not null && !ArchiveCapabilities.CanCreateWithPassword(format))
            throw new StageException("CLINE0003", LanguageManager.Get("PasswordFormatUnsupported")); //CLINE0003
        CompressionOptions options = new(password, advanced, level, method, dictionary, threads, memory, volume);
        await new ArchiveWorkerClient().CreateAsync(sources, output, format, null, token, password, options);
        Console.WriteLine(string.Format(LanguageManager.Get("CliCreated"), output));
    }

    private static async Task ExtractAsync(string[] args, CancellationToken token)
    {
        Parsed parsed = Parse(args, "password", "password-env", "encoding", "entry", "on-conflict", "on-issue");
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
        Parsed parsed = Parse(args, "password", "password-env", "encoding", "json");
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

    private static string RequireArchive(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException(LanguageManager.Get("CliArchiveMissing"), full);
        if (!ArchiveCapabilities.CanOpen(FileDetector.DetectFileType(full)))
            throw new StageException("CLINE0005", LanguageManager.Get("CliUnsupportedArchive")); //CLINE0005
        return full;
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
            "iso" => FileDetector.FileType.Iso, "wim" => FileDetector.FileType.Wim,
            _ => FileDetector.FileType.Unknown
        };
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t")
        .Replace("\r", "\\r").Replace("\n", "\\n");

    private static StageException Usage(string command) => new("CLINE0007",
        LanguageManager.Get("CliInvalidArguments") + Environment.NewLine +
        LanguageManager.Get(Commands.Contains(command) ? "CliHelp_" + command : "CliHelp")); //CLINE0007

    private static void PrintHelp(string? command)
    {
        string key = command?.ToLowerInvariant() switch
        {
            "compress" => "CliHelp_compress", "extract" => "CliHelp_extract",
            "list" => "CliHelp_list", _ => "CliHelp"
        };
        Console.WriteLine(LanguageManager.Get(key));
    }

    private static Parsed Parse(string[] args, params string[] allowed)
    {
        Parsed parsed = new(allowed.Contains("entry") ? "extract" : allowed.Contains("format") ? "compress" : "list");
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
                string value = name == "json" ? "true" :
                    ++index < args.Length ? args[index] : throw Usage(parsed.Command);
                if (name != "entry" && parsed.Has(name)) throw Usage(parsed.Command);
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
