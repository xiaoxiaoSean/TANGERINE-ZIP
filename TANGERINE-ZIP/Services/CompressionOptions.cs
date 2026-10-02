namespace TANGERINE_ZIP.Services;

// Only validated values cross the worker boundary. The default profile preserves the
// existing format-specific writer when no advanced setting is requested.
internal sealed record CompressionOptions(string? Password = null, bool Advanced = false,
    int Level = 5, string Method = "Default", int DictionaryMiB = 16,
    int Threads = 0, int MemoryLimitMiB = 0, int VolumeMiB = 0,
    bool SelfExtracting = false, string SolidMode = "Default",
    int RecoveryPercent = 0, string[]? ExcludePatterns = null,
    IsoCreationOptions? Iso = null);

// These settings are intentionally separate from the compression profile:
// an ISO image is a filesystem with optional El Torito boot metadata.
internal sealed record IsoCreationOptions(string VolumeIdentifier, string ManufacturerId,
    bool UseJoliet, bool TrackEqualSourceFiles, bool Bootable,
    string? BootImagePath, string BootEmulation, int LoadSegment,
    bool UpdateIsolinuxBootTable);
