using System.Runtime.InteropServices;

namespace DonutInstaller;

static class Program
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    [STAThread]
    static int Main(string[] args)
    {
        var options = ParseArgs(args);

        if (options.Silent)
            return RunSilent(options);

        using var mutex = new Mutex(true, "DonutButBetterInstaller", out var firstInstance);
        if (!firstInstance)
            return 0;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(options));
        return 0;
    }

    // Options for testing and scripting:
    //   --minecraft-dir <folder>   install somewhere other than %APPDATA%\.minecraft
    //   --manifest <url or file>   use this manifest instead of the website
    //   --no-launcher-check        don't require or wait for the Minecraft Launcher
    //   --silent                   no window; exit code 0 on success
    static InstallOptions ParseArgs(string[] args)
    {
        var options = new InstallOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--minecraft-dir" when i + 1 < args.Length:
                    options.MinecraftDir = Path.GetFullPath(args[++i]);
                    break;
                case "--manifest" when i + 1 < args.Length:
                    options.ManifestSource = args[++i];
                    break;
                case "--no-launcher-check":
                    options.SkipLauncherCheck = true;
                    break;
                case "--silent":
                    options.Silent = true;
                    break;
            }
        }
        return options;
    }

    static int RunSilent(InstallOptions options)
    {
        AttachConsole(-1);
        try
        {
            string? lastLine = null;
            var progress = new Progress<Status>(status =>
            {
                if (status.Text != lastLine)
                    Console.WriteLine(lastLine = status.Text);
            });
            var result = new Installer(options, progress).Run(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"OK {result.Version}{(result.Offline ? " (offline)" : "")}");
            return 0;
        }
        catch (Exception e)
        {
            Log.Write("failed: " + e);
            Console.WriteLine("FAILED: " + e.Message);
            return 1;
        }
    }
}
