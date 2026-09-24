using TANGERINE_ZIP.Tools;

namespace TANGERINE_ZIP.Services;

// Stage head: PRSVS
internal static class PreviewSaveService
{
    public static async Task SaveAsAsync(Window owner, PreviewPayload payload, string sourceArchivePath)
    {
        try
        {
            SaveFileDialog dialog = new()
            {
                Title = LanguageManager.Get("PreviewSaveAs"),
                FileName = Path.GetFileName(payload.EntryKey),
                Filter = LanguageManager.Get("PreviewSaveFilter"),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(owner) != true) return;
            string destination = Path.GetFullPath(dialog.FileName);
            if (destination.Equals(Path.GetFullPath(sourceArchivePath), StringComparison.OrdinalIgnoreCase))
                throw new StageException("PRSVS0001", LanguageManager.Get("PreviewCannotOverwriteArchive")); //PRSVS0001
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                    await output.WriteAsync(payload.Buffer.AsMemory(0, payload.Length));
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        catch (StageException) { throw; }
        catch (Exception exception)
        {
            throw new StageException("PRSVS0002", exception.Message, exception); //PRSVS0002
        }
    }
}
