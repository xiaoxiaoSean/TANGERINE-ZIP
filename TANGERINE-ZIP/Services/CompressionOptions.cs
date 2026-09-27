namespace TANGERINE_ZIP.Services;

// Only validated values cross the worker boundary. The default profile preserves the
// existing format-specific writer when no advanced setting is requested.
internal sealed record CompressionOptions(string? Password = null, bool Advanced = false,
    int Level = 5, string Method = "Default", int DictionaryMiB = 16,
    int Threads = 0, int MemoryLimitMiB = 0, int VolumeMiB = 0);
