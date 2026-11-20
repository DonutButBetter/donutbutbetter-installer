using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DonutInstaller;

// Everything that touches the official Minecraft Launcher: finding it, starting it,
// and keeping our profile in launcher_profiles.json.
public static class MinecraftLauncher
{
    const string StorePackage = "Microsoft.4297127D64EC6_8wekyb3d8bbwe";
    const string ProfileKey = "donutbutbetter";

    public static string DefaultMinecraftDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    // The desktop installer puts an exe in Program Files; the Microsoft Store version has none.
    static string? FindExe()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        return folders
            .Select(folder => Path.Combine(folder, "Minecraft Launcher", "MinecraftLauncher.exe"))
            .FirstOrDefault(File.Exists);
    }

    static bool IsStoreVersionInstalled()
    {
        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", StorePackage);
        return Directory.Exists(packages);
    }

    public static bool IsInstalled() => FindExe() != null || IsStoreVersionInstalled();

    public static bool IsRunning() => Process.GetProcessesByName("MinecraftLauncher").Length > 0;

    public static void Start()
    {
        var exe = FindExe();
        if (exe != null)
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)! });
            return;
        }
        if (IsStoreVersionInstalled())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{StorePackage}!Minecraft"));
            return;
        }
        throw new InstallException("The Minecraft Launcher isn't installed.", launcherMissing: true);
    }

    // Adds or refreshes our profile and leaves everything else in the file untouched.
    public static void WriteProfile(string minecraftDir, string gameDir, string versionId, string name, string iconDataUri)
    {
        var path = Path.Combine(minecraftDir, "launcher_profiles.json");

        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                    ?? throw new JsonException("not an object");
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                // better to stop than to overwrite someone's launcher settings
                throw new InstallException(
                    "The Minecraft Launcher's profile file looks damaged. Open the Minecraft Launcher once, close it, then run this again.");
            }
        }
        else
        {
            root = new JsonObject { ["version"] = 3 };
        }

        if (root["profiles"] is not JsonObject profiles)
        {
            profiles = new JsonObject();
            root["profiles"] = profiles;
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var profile = profiles[ProfileKey] as JsonObject ?? new JsonObject();
        profile["name"] = name;
        profile["type"] = "custom";
        profile["created"] ??= now;
        profile["lastUsed"] = now;
        profile["icon"] = iconDataUri;
        profile["lastVersionId"] = versionId;
        profile["gameDir"] = gameDir;
        profiles[ProfileKey] = profile;

        Directory.CreateDirectory(minecraftDir);
        if (File.Exists(path))
        {
            var backup = path + ".bak";
            if (!File.Exists(backup))
                File.Copy(path, backup);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.Move(temp, path, overwrite: true);
    }
}
