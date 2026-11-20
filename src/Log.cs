namespace DonutInstaller;

public static class Log
{
    static readonly object gate = new();
    static string? path;

    public static string? FilePath => path;

    public static void Init(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, "installer.log");
            // start fresh once it gets big
            if (File.Exists(path) && new FileInfo(path).Length > 200_000)
                File.Delete(path);
        }
        catch (IOException) { path = null; }
        catch (UnauthorizedAccessException) { path = null; }
    }

    public static void Write(string message)
    {
        if (path == null)
            return;
        lock (gate)
        {
            try { File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"); }
            catch (IOException) { }
        }
    }
}
