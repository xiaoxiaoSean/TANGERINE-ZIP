namespace TANGERINE_ZIP.Services;

internal static class ArchiveOutput
{
    public static bool Exists(string path) => File.Exists(path) || File.Exists(path + ".001") ||
        File.Exists(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".part1.rar"));
}
