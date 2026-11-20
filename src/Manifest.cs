using System.Text.Json;
using System.Text.RegularExpressions;

namespace DonutInstaller;

public class PackFile
{
    public string Path { get; set; } = "";
    public string Sha512 { get; set; } = "";
    public long Size { get; set; }
    public List<string> Urls { get; set; } = new();
}

public class Manifest
{
    public string Version { get; set; } = "";
    public string Minecraft { get; set; } = "";
    public string FabricLoader { get; set; } = "";
    public string ProfileName { get; set; } = "DonutButBetter";
    public PackFile Loader { get; set; } = new();
    public List<PackFile> Files { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static Manifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(json, Options)
            ?? throw new InstallException("The pack manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    void Validate()
    {
        if (Version.Length == 0 || Minecraft.Length == 0 || FabricLoader.Length == 0)
            throw new InstallException("The pack manifest is missing its version info.");
        if (Files.Count == 0)
            throw new InstallException("The pack manifest lists no files.");

        CheckFile(Loader, isLoader: true);
        foreach (var file in Files)
            CheckFile(file, isLoader: false);
    }

    static void CheckFile(PackFile file, bool isLoader)
    {
        if (!Regex.IsMatch(file.Sha512, "^[0-9a-fA-F]{128}$"))
            throw new InstallException($"The pack manifest has a bad hash for {file.Path}.");
        if (file.Urls.Count == 0)
            throw new InstallException($"The pack manifest has no download link for {file.Path}.");
        if (isLoader)
            return;

        // mods only ever go in the mods folder of the pack's own game directory
        var okPath = file.Path.StartsWith("mods/", StringComparison.Ordinal)
            && file.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && file.Path.IndexOfAny(new[] { '\\', ':', '\0' }) < 0
            && !file.Path.Split('/').Any(part => part is "" or "." or "..");
        if (!okPath)
            throw new InstallException($"The pack manifest has an unsafe file path: {file.Path}");
    }
}

// An error whose message is fine to show to the player as-is.
public class InstallException : Exception
{
    public bool LauncherMissing { get; }

    public InstallException(string message, bool launcherMissing = false) : base(message)
    {
        LauncherMissing = launcherMissing;
    }
}
