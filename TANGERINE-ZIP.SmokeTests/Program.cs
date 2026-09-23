using TANGERINE_ZIP.Services;
using TANGERINE_ZIP.Tools;
using System.IO;

if (args.Length == 2 && args[0] == "--ui-probe")
{
    UiProbe.Run(args[1]);
    return;
}

string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string source = Path.Combine(repositoryRoot, "testfile", "1", "Linux教程.pdf");
if (!File.Exists(source)) throw new FileNotFoundException("The requested Chinese-name test document is missing.", source);

string testRoot = Path.Combine(Path.GetTempPath(), "TangerineZipTargetedTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
try
{
    ArchiveService service = new();
    // An independent ZIP reader must honor the emitted UTF-8 flag, not a forced decoder.
    string smallSource = Path.Combine(testRoot, "中文_日本語_한국어_العربية_Русский_é_😀.txt");
    byte[] originalBytes = System.Text.Encoding.UTF8.GetBytes("中文内容 Traditional 繁體字\r\n");
    File.WriteAllBytes(smallSource, originalBytes);
    foreach (FileDetector.FileType format in new[] { FileDetector.FileType.Zip, FileDetector.FileType.SevenZip, FileDetector.FileType.Tar, FileDetector.FileType.GZip, FileDetector.FileType.BZip2, FileDetector.FileType.Xz, FileDetector.FileType.Lz4, FileDetector.FileType.Zstd, FileDetector.FileType.Iso, FileDetector.FileType.Wim })
    {
        string archivePath = Path.Combine(testRoot, $"{Path.GetFileName(smallSource)}.{format}");
        await service.CreateAsync([smallSource], archivePath, format, null, CancellationToken.None);
        if (format == FileDetector.FileType.Zip)
        {
            byte[] zipHeader = File.ReadAllBytes(archivePath);
            AssertZipEntryEncoding(zipHeader, Path.GetFileName(smallSource));
            using var independentZip = System.IO.Compression.ZipFile.OpenRead(archivePath);
            if (independentZip.Entries.Single().FullName != Path.GetFileName(smallSource))
                throw new InvalidOperationException("ZIP UTF-8 interoperability failed.");
        }
        string outputDirectory = Path.Combine(testRoot, $"roundtrip-{format}");
        await service.ExtractAsync(archivePath, outputDirectory, null, OverwritePolicy.OverwriteAll, null, CancellationToken.None);
        string extracted = Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories).Single();
        if (!File.ReadAllBytes(extracted).SequenceEqual(originalBytes)) throw new InvalidOperationException($"Payload mismatch: {format}");
        if (Path.GetFileName(extracted) != Path.GetFileName(smallSource)) throw new InvalidOperationException($"Filename mismatch: {format}: {extracted}");
        Console.WriteLine($"PASS Unicode name and exact payload: {format}");
    }

    ArchiveWorkerClient worker = new();
    string workerZip = Path.Combine(testRoot, "worker.zip");
    await worker.CreateAsync([smallSource], workerZip, FileDetector.FileType.Zip, null, CancellationToken.None);
    var workerEntries = await worker.ListAsync(workerZip, CancellationToken.None);
    if (workerEntries.Single().Key != Path.GetFileName(smallSource)) throw new InvalidOperationException("Worker protocol corrupted Unicode.");
    Console.WriteLine("PASS worker creation and Unicode pipe protocol");

    string cancelSource = Path.Combine(testRoot, "cancel-input.bin");
    File.WriteAllBytes(cancelSource, System.Security.Cryptography.RandomNumberGenerator.GetBytes(8 * 1024 * 1024));
    using CancellationTokenSource stop = new();
    InlineProgress<ArchiveProgress> stopProgress = new(_ => stop.Cancel());
    try
    {
        await worker.CreateAsync([cancelSource], Path.Combine(testRoot, "cancel-output.bz2"), FileDetector.FileType.BZip2, stopProgress, stop.Token);
        throw new InvalidOperationException("The forced stop was not observed.");
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
        Console.WriteLine("PASS force-stop worker during compression");
    }

    RarToolService rarToolService = new();
    if (rarToolService.IsAvailable)
    {
        string rarPath = Path.Combine(testRoot, "unicode.rar");
        List<int> percentages = [];
        await worker.CreateAsync([smallSource], rarPath, FileDetector.FileType.Rar,
            new InlineProgress<ArchiveProgress>(item => percentages.Add(item.Percentage)), CancellationToken.None);
        string rarOutput = Path.Combine(testRoot, "rar-output");
        await service.ExtractAsync(rarPath, rarOutput, null, OverwritePolicy.OverwriteAll, null, CancellationToken.None);
        if (!File.ReadAllBytes(Path.Combine(rarOutput, Path.GetFileName(smallSource))).SequenceEqual(originalBytes))
            throw new InvalidOperationException("RAR Unicode round trip failed.");
        if (percentages.Zip(percentages.Skip(1)).Any(pair => pair.First > pair.Second))
            throw new InvalidOperationException("RAR progress moved backwards.");
        Console.WriteLine("PASS official RAR Unicode round trip and monotonic progress");
    }
    string startupMarkerPath = rarToolService.SkipStartupCheckMarkerPath;
    bool markerExistedBeforeTest = File.Exists(startupMarkerPath);
    try
    {
        if (!markerExistedBeforeTest) File.WriteAllBytes(startupMarkerPath, []);
        if (rarToolService.ShouldCheckAtStartup)
            throw new InvalidOperationException("The RAR startup-check marker was not honored.");
        Console.WriteLine("PASS RAR startup-check marker");
    }
    finally
    {
        if (!markerExistedBeforeTest && File.Exists(startupMarkerPath)) File.Delete(startupMarkerPath);
    }

    string zipPath = Path.Combine(testRoot, "unicode.zip");
    await service.CreateAsync([source], zipPath, FileDetector.FileType.Zip, null, CancellationToken.None);
    IReadOnlyList<ArchiveEntryInfo> zipEntries = await service.ListAsync(zipPath, CancellationToken.None);
    if (!zipEntries.Any(entry => entry.Key == "Linux教程.pdf"))
        throw new InvalidOperationException("The ZIP entry name was not preserved as UTF-8.");
    Console.WriteLine("PASS Chinese ZIP entry name");

    string firstTar = Path.Combine(testRoot, "first.tar");
    await service.CreateAsync([source], firstTar, FileDetector.FileType.Tar, null, CancellationToken.None);
    string nestedBzip = Path.Combine(testRoot, "nested-without-extension");
    await service.CreateAsync([firstTar], nestedBzip, FileDetector.FileType.BZip2, null, CancellationToken.None);
    NestedTarInfo singleNested = await service.AnalyzeNestedTarAsync(nestedBzip, CancellationToken.None);
    if (!singleNested.FlattenAutomatically) throw new InvalidOperationException("Single nested TAR was not detected by content.");
    IReadOnlyList<ArchiveEntryInfo> nestedEntries = await service.ListNestedTarAsync(nestedBzip, singleNested.TarEntryKeys[0], CancellationToken.None);
    if (!nestedEntries.Any(entry => entry.Key == "Linux教程.pdf"))
        throw new InvalidOperationException("Single nested TAR did not expose its inner files.");
    Console.WriteLine("PASS single nested TAR content detection");

    string secondTar = Path.Combine(testRoot, "second.tar");
    await service.CreateAsync([source], secondTar, FileDetector.FileType.Tar, null, CancellationToken.None);
    string multipleArchive = Path.Combine(testRoot, "multiple.zip");
    await service.CreateAsync([firstTar, secondTar], multipleArchive, FileDetector.FileType.Zip, null, CancellationToken.None);
    NestedTarInfo multipleNested = await service.AnalyzeNestedTarAsync(multipleArchive, CancellationToken.None);
    if (multipleNested.TarEntryKeys.Count != 2 || multipleNested.FlattenAutomatically)
        throw new InvalidOperationException("Multiple nested TAR files were not kept as separate choices.");
    string extractionRoot = Path.Combine(testRoot, "multiple-output");
    await service.ExtractNestedTarsAsync(multipleArchive, multipleNested.TarEntryKeys, extractionRoot, null, true, OverwritePolicy.OverwriteAll, null, CancellationToken.None);
    if (!File.Exists(Path.Combine(extractionRoot, "first", "Linux教程.pdf")) ||
        !File.Exists(Path.Combine(extractionRoot, "second", "Linux教程.pdf")))
        throw new InvalidOperationException("Multiple nested TAR extraction did not create per-TAR folders.");
    Console.WriteLine("PASS multiple nested TAR selection layout");
}
finally
{
    string fullTestRoot = Path.GetFullPath(testRoot);
    string safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TangerineZipTargetedTests")) + Path.DirectorySeparatorChar;
    if (fullTestRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullTestRoot))
        Directory.Delete(fullTestRoot, true);
}

static void AssertZipEntryEncoding(byte[] archiveBytes, string expectedName)
{
    byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedName);
    int localOffset = FindSignature(archiveBytes, 0x04034B50);
    int centralOffset = FindSignature(archiveBytes, 0x02014B50);
    if (localOffset < 0 || centralOffset < 0) throw new InvalidOperationException("ZIP headers are missing.");
    if ((BitConverter.ToUInt16(archiveBytes, localOffset + 6) & 0x800) == 0 ||
        (BitConverter.ToUInt16(archiveBytes, centralOffset + 8) & 0x800) == 0)
        throw new InvalidOperationException("ZIP does not declare UTF-8 in both entry headers.");
    int localNameLength = BitConverter.ToUInt16(archiveBytes, localOffset + 26);
    int centralNameLength = BitConverter.ToUInt16(archiveBytes, centralOffset + 28);
    if (!archiveBytes.AsSpan(localOffset + 30, localNameLength).SequenceEqual(expectedBytes) ||
        !archiveBytes.AsSpan(centralOffset + 46, centralNameLength).SequenceEqual(expectedBytes))
        throw new InvalidOperationException("ZIP entry-name bytes are not UTF-8.");
}

static int FindSignature(byte[] data, uint signature)
{
    for (int index = 0; index <= data.Length - sizeof(uint); index++)
        if (BitConverter.ToUInt32(data, index) == signature) return index;
    return -1;
}
