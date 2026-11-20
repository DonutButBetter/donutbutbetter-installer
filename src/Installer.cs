using System.Text.Json;

namespace DonutInstaller;

public record Status(string Text, double? Fraction = null);

public record InstallResult(string Version, bool Offline);

public class InstallOptions
{
    public string MinecraftDir { get; set; } = MinecraftLauncher.DefaultMinecraftDir;
    public string? ManifestSource { get; set; }
    public bool SkipLauncherCheck { get; set; }
    public bool Silent { get; set; }
}

public class Installer
{
    static readonly string[] ManifestUrls =
    {
        "https://donutbutbetter-website.donutbutbetter.workers.dev/pack/manifest.json",
        "https://github.com/DonutButBetter/donutbutbetter-website/releases/latest/download/manifest.json",
    };

    const string StateFileName = "donutbutbetter-pack.json";

    readonly InstallOptions options;
    readonly Downloader downloader = new();
    readonly IProgress<Status> progress;
    bool serverAnswered;

    string GameDir => Path.Combine(options.MinecraftDir, "donutbutbetter");
    string StatePath => Path.Combine(GameDir, StateFileName);

    public Installer(InstallOptions options, IProgress<Status> progress)
    {
        this.options = options;
        this.progress = progress;
    }

    public async Task<InstallResult> Run(CancellationToken ct)
    {
        Log.Init(GameDir);
        Log.Write($"starting, minecraft dir {options.MinecraftDir}");

        if (!options.SkipLauncherCheck && !MinecraftLauncher.IsInstalled())
        {
            throw new InstallException(
                "The Minecraft Launcher isn't installed on this computer. Install it first, then run this again.",
                launcherMissing: true);
        }

        progress.Report(new Status("Checking for updates..."));
        var manifest = await LoadManifest(ct);
        var previous = ReadState();

        if (manifest == null)
        {
            if (previous == null)
            {
                throw new InstallException(serverAnswered
                    ? "Connected, but the DonutButBetter pack isn't published yet (or is broken). Tell a DonutButBetter admin."
                    : "No internet connection. Check your connection and try again.");
            }
            Log.Write("offline, keeping installed version " + previous.Version);
            return new InstallResult(previous.Version, Offline: true);
        }
        Log.Write($"pack version {manifest.Version}");

        Directory.CreateDirectory(Path.Combine(GameDir, "mods"));

        var versionId = await InstallLoader(manifest, ct);
        await InstallFiles(manifest, ct);
        RemoveOldFiles(manifest, previous);
        WriteState(new PackState { Version = manifest.Version, Files = manifest.Files.Select(f => f.Path).ToList() });

        await WaitForLauncherToClose(ct);
        progress.Report(new Status("Adding the DonutButBetter profile..."));
        MinecraftLauncher.WriteProfile(options.MinecraftDir, GameDir, versionId, manifest.ProfileName, LoadIcon());

        Log.Write("done");
        return new InstallResult(manifest.Version, Offline: false);
    }

    // The website is tried first, GitHub second. Returns null if neither answers.
    async Task<Manifest?> LoadManifest(CancellationToken ct)
    {
        var sources = options.ManifestSource != null ? new[] { options.ManifestSource } : ManifestUrls;
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = File.Exists(source)
                    ? await File.ReadAllTextAsync(source, ct)
                    : await downloader.GetText(source, TimeSpan.FromSeconds(15), ct);
                return Manifest.Parse(json);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // a status code or a bad manifest means the server did answer, so it isn't an internet problem
                if (e is not (HttpRequestException { StatusCode: null } or OperationCanceledException))
                    serverAnswered = true;
                Log.Write($"manifest from {source} failed: {e.Message}");
            }
        }
        return null;
    }

    async Task<string> InstallLoader(Manifest manifest, CancellationToken ct)
    {
        var id = $"fabric-loader-{manifest.FabricLoader}-{manifest.Minecraft}";
        var path = Path.Combine(options.MinecraftDir, "versions", id, id + ".json");

        if (!await Downloader.MatchesHash(path, manifest.Loader.Sha512, ct))
        {
            progress.Report(new Status($"Setting up Fabric {manifest.FabricLoader}..."));
            await downloader.Fetch(manifest.Loader, path, _ => { }, ct);
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        var actual = doc.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        if (actual != id)
            throw new InstallException("The Fabric download doesn't match this pack. Please tell a DonutButBetter admin.");

        // the official installer leaves an empty jar next to the json, so do the same
        var jar = Path.ChangeExtension(path, ".jar");
        if (!File.Exists(jar))
            File.Create(jar).Dispose();

        return id;
    }

    async Task InstallFiles(Manifest manifest, CancellationToken ct)
    {
        progress.Report(new Status("Checking installed mods..."));
        var missing = new List<PackFile>();
        foreach (var file in manifest.Files)
        {
            if (!await Downloader.MatchesHash(DestinationOf(file), file.Sha512, ct))
                missing.Add(file);
        }

        var totalBytes = Math.Max(1, missing.Sum(f => f.Size));
        long finishedBytes = 0;
        foreach (var file in missing)
        {
            var name = Path.GetFileName(file.Path);
            var before = finishedBytes;
            progress.Report(new Status($"Downloading {name}", (double)before / totalBytes));
            await downloader.Fetch(file, DestinationOf(file), received =>
                progress.Report(new Status($"Downloading {name}", Math.Min(1.0, (double)(before + received) / totalBytes))), ct);
            finishedBytes += file.Size;
        }
    }

    string DestinationOf(PackFile file) =>
        Path.Combine(GameDir, file.Path.Replace('/', Path.DirectorySeparatorChar));

    // Only deletes files this installer put there for an earlier version,
    // so mods the player added themselves stay.
    void RemoveOldFiles(Manifest manifest, PackState? previous)
    {
        if (previous == null)
            return;
        var keep = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in previous.Files.Where(path => !keep.Contains(path)))
        {
            if (!old.StartsWith("mods/", StringComparison.Ordinal) || old.Contains(".."))
                continue;
            try
            {
                File.Delete(Path.Combine(GameDir, old.Replace('/', Path.DirectorySeparatorChar)));
                Log.Write("removed old " + old);
            }
            catch (IOException e)
            {
                throw new InstallException($"Couldn't remove the old mod {Path.GetFileName(old)} ({e.Message}). Close Minecraft and try again.");
            }
        }
    }

    // The launcher rewrites launcher_profiles.json when it closes, which would undo our change.
    async Task WaitForLauncherToClose(CancellationToken ct)
    {
        if (options.SkipLauncherCheck || !MinecraftLauncher.IsRunning())
            return;
        if (options.Silent)
            throw new InstallException("Close the Minecraft Launcher and run this again.");

        while (MinecraftLauncher.IsRunning())
        {
            progress.Report(new Status("Please close the Minecraft Launcher so the profile can be added..."));
            await Task.Delay(1000, ct);
        }
    }

    static string LoadIcon()
    {
        using var stream = typeof(Installer).Assembly.GetManifestResourceStream("donut.png")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return "data:image/png;base64," + Convert.ToBase64String(buffer.ToArray());
    }

    class PackState
    {
        public string Version { get; set; } = "";
        public List<string> Files { get; set; } = new();
    }

    PackState? ReadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<PackState>(File.ReadAllText(StatePath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    void WriteState(PackState state) =>
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}
