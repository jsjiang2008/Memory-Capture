namespace MemoryCapture;

/// <summary>The only service allowed to create image files; called only after an explicit Save action.</summary>
public static class ExportService
{
    public static string SaveUnique(string desiredPath, byte[] bytes, CancellationToken token = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(desiredPath))!;
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var extension = Path.GetExtension(desiredPath);
        for (int number = 0; number < 10000; number++)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, stem + (number == 0 ? "" : $" ({number})") + extension);
            FileStream output;
            try { output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try
            {
                using (output)
                {
                    for (int offset = 0; offset < bytes.Length; offset += 81920)
                    {
                        token.ThrowIfCancellationRequested();
                        output.Write(bytes, offset, Math.Min(81920, bytes.Length - offset));
                    }
                    output.Flush();
                }
                return path;
            }
            catch
            {
                output.Dispose();
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
        throw new IOException("Too many files share this name. Choose a different name.");
    }
}
