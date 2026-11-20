using System.Security.Cryptography;

namespace DonutInstaller;

public class Downloader
{
    const int AttemptsPerUrl = 2;

    readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public Downloader()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DonutButBetter-Installer/1.0");
        http.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
    }

    public async Task<string> GetText(string url, TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        return await http.GetStringAsync(url, limit.Token);
    }

    public static async Task<string> HashFile(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA512.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    public static async Task<bool> MatchesHash(string path, string sha512, CancellationToken ct)
    {
        if (!File.Exists(path))
            return false;
        return string.Equals(await HashFile(path, ct), sha512, StringComparison.OrdinalIgnoreCase);
    }

    // Tries each mirror in order, retrying a couple of times, and only moves the file into
    // place once its hash matches. onBytes reports bytes received so far for the current try.
    public async Task Fetch(PackFile file, string destination, Action<long> onBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";
        var lastError = "no download links";

        foreach (var url in file.Urls)
        {
            for (var attempt = 1; attempt <= AttemptsPerUrl; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                onBytes(0);
                try
                {
                    await Download(url, partial, onBytes, ct);
                    var actual = await HashFile(partial, ct);
                    if (!string.Equals(actual, file.Sha512, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("the downloaded file is corrupted");

                    File.Move(partial, destination, overwrite: true);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    TryDelete(partial);
                    throw;
                }
                catch (Exception e)
                {
                    lastError = e.Message;
                    Log.Write($"download failed ({url}, try {attempt}): {e.Message}");
                    TryDelete(partial);
                    if (attempt < AttemptsPerUrl)
                        await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                }
            }
        }

        throw new InstallException(
            $"Couldn't download {System.IO.Path.GetFileName(file.Path)} ({lastError}). Check your internet connection and try again, or tell a DonutButBetter admin if it keeps happening.");
    }

    async Task Download(string url, string destination, Action<long> onBytes, CancellationToken ct)
    {
        // a connection that stops sending data shouldn't hang the installer forever
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, stall.Token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), stall.Token);
            total += read;
            onBytes(total);
            stall.CancelAfter(TimeSpan.FromSeconds(30));
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
