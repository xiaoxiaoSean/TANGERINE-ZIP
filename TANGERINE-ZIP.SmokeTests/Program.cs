using System.Text;
using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;

string testRoot = Path.Combine(Path.GetTempPath(), "TangerineZipSmokeTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
try
{
    string source = Path.Combine(testRoot, "payload.txt");
    const string content = "Tangerine ZIP smoke test / 橘子压缩测试";
    await File.WriteAllTextAsync(source, content, new UTF8Encoding(false));
    ArchiveService service = new();
    FileDetector.FileType[] formats =
    [
        FileDetector.FileType.Zip, FileDetector.FileType.SevenZip, FileDetector.FileType.Tar,
        FileDetector.FileType.GZip, FileDetector.FileType.BZip2, FileDetector.FileType.Xz,
        FileDetector.FileType.Lz4, FileDetector.FileType.Zstd, FileDetector.FileType.Iso,
        FileDetector.FileType.Wim
    ];
    Dictionary<FileDetector.FileType, string> extensions = new()
    {
        [FileDetector.FileType.Zip] = ".zip", [FileDetector.FileType.SevenZip] = ".7z",
        [FileDetector.FileType.Tar] = ".tar", [FileDetector.FileType.GZip] = ".gz",
        [FileDetector.FileType.BZip2] = ".bz2", [FileDetector.FileType.Xz] = ".xz",
        [FileDetector.FileType.Lz4] = ".lz4", [FileDetector.FileType.Zstd] = ".zst",
        [FileDetector.FileType.Iso] = ".iso", [FileDetector.FileType.Wim] = ".wim"
    };

    foreach (FileDetector.FileType format in formats)
    {
        string archive = Path.Combine(testRoot, format + extensions[format]);
        string extractDirectory = Path.Combine(testRoot, "out-" + format);
        await service.CreateAsync([source], archive, format, null, CancellationToken.None);
        if (FileDetector.DetectFileType(archive) != format)
            throw new InvalidOperationException($"Detection failed for {format}.");
        IReadOnlyList<ArchiveEntryInfo> entries = await service.ListAsync(archive, CancellationToken.None);
        if (entries.Count == 0)
            throw new InvalidOperationException($"Listing failed for {format}.");
        await service.ExtractAsync(archive, extractDirectory, null, OverwritePolicy.OverwriteAll, null, CancellationToken.None);
        string? extracted = Directory.EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories).FirstOrDefault();
        if (extracted is null || await File.ReadAllTextAsync(extracted) != content)
            throw new InvalidOperationException($"Round-trip failed for {format}.");
        Console.WriteLine($"PASS {format}");
    }

    try
    {
        await service.CreateAsync([source], Path.Combine(testRoot, "unsupported.rar"), FileDetector.FileType.Rar, null, CancellationToken.None);
        throw new InvalidOperationException("RAR creation must be rejected.");
    }
    catch (StageException exception) when (exception.StageCode == "ARCSV0003")
    {
        Console.WriteLine("PASS Rar create capability guard");
    }
}
finally
{
    string fullTestRoot = Path.GetFullPath(testRoot);
    string safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TangerineZipSmokeTests")) + Path.DirectorySeparatorChar;
    if (fullTestRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullTestRoot))
        Directory.Delete(fullTestRoot, true);
}
