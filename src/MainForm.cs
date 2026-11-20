using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DonutInstaller;

public class MainForm : Form
{
    static readonly Color Background = Color.FromArgb(24, 24, 28);
    static readonly Color Panel = Color.FromArgb(40, 40, 46);
    static readonly Color Accent = Color.FromArgb(240, 150, 60);
    static readonly Color TextColor = Color.FromArgb(235, 235, 240);
    static readonly Color MutedText = Color.FromArgb(150, 150, 160);
    static readonly Color ErrorColor = Color.FromArgb(240, 100, 100);

    readonly InstallOptions options;
    readonly Label title = new();
    readonly Label status = new();
    readonly Label note = new();
    readonly ProgressTrack track = new();
    readonly Button primary = new();
    readonly Button secondary = new();

    CancellationTokenSource? cancel;
    Action onPrimary = () => { };
    Action onSecondary = () => { };

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public MainForm(InstallOptions options)
    {
        this.options = options;

        Text = "DonutButBetter";
        ClientSize = new Size(480, 250);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var logo = new PictureBox
        {
            Image = LoadLogo(),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Bounds = new Rectangle(24, 24, 64, 64),
        };

        title.Text = "DonutButBetter";
        title.Font = new Font("Segoe UI Semibold", 18f);
        title.Bounds = new Rectangle(104, 24, 350, 36);

        status.Bounds = new Rectangle(106, 62, 350, 44);
        status.ForeColor = MutedText;

        track.Bounds = new Rectangle(24, 112, 432, 8);

        note.Bounds = new Rectangle(24, 132, 432, 44);
        note.ForeColor = MutedText;

        StyleButton(primary, filled: true);
        primary.Bounds = new Rectangle(24, 192, 240, 40);
        primary.Click += (_, _) => onPrimary();

        StyleButton(secondary, filled: false);
        secondary.Bounds = new Rectangle(280, 192, 176, 40);
        secondary.Click += (_, _) => onSecondary();

        Controls.AddRange(new Control[] { logo, title, status, track, note, primary, secondary });

        Shown += async (_, _) => await Start();
        FormClosing += (_, _) => cancel?.Cancel();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); // dark title bar, ignored where unsupported
    }

    static Image LoadLogo()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("donut.png")!;
        return new Bitmap(stream);
    }

    static void StyleButton(Button button, bool filled)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = filled ? Accent : Panel;
        button.ForeColor = filled ? Color.FromArgb(30, 20, 10) : TextColor;
        button.Font = new Font("Segoe UI Semibold", 10.5f);
        button.Cursor = Cursors.Hand;
    }

    async Task Start()
    {
        cancel = new CancellationTokenSource();
        ShowWorking();

        var progress = new Progress<Status>(update =>
        {
            status.Text = update.Text;
            track.Fraction = update.Fraction;
        });

        try
        {
            var result = await new Installer(options, progress).Run(cancel.Token);
            ShowReady(result);
        }
        catch (OperationCanceledException)
        {
            // window was closed
        }
        catch (InstallException e)
        {
            Log.Write("failed: " + e.Message);
            ShowError(e.Message, e.LauncherMissing);
        }
        catch (Exception e)
        {
            Log.Write("failed: " + e);
            ShowError($"Something went wrong: {e.Message}", launcherMissing: false);
        }
    }

    void ShowWorking()
    {
        status.ForeColor = MutedText;
        status.Text = "Starting...";
        track.Visible = true;
        track.Fraction = null;
        note.Text = "";
        primary.Visible = false;
        secondary.Visible = false;
    }

    void ShowReady(InstallResult result)
    {
        status.ForeColor = TextColor;
        status.Text = $"Ready - version {result.Version}";
        track.Visible = false;
        note.ForeColor = MutedText;
        note.Text = result.Offline
            ? "No internet connection, so the installed version was kept. Updates will be checked next time."
            : "Open the Minecraft Launcher, pick the DonutButBetter profile and press Play.";

        SetButtons("Open Minecraft Launcher", OpenLauncher, "Close", Close);
    }

    void ShowError(string message, bool launcherMissing)
    {
        status.ForeColor = ErrorColor;
        status.Text = "Couldn't finish the install";
        track.Visible = false;
        note.ForeColor = TextColor;
        note.Text = message;

        if (launcherMissing)
            SetButtons("Get the Minecraft Launcher", () => OpenUrl("https://www.minecraft.net/download"), "Try again", RetryClicked);
        else
            SetButtons("Try again", RetryClicked, "Close", Close);
    }

    void SetButtons(string primaryText, Action primaryAction, string secondaryText, Action secondaryAction)
    {
        primary.Text = primaryText;
        onPrimary = primaryAction;
        secondary.Text = secondaryText;
        onSecondary = secondaryAction;
        primary.Visible = true;
        secondary.Visible = true;
    }

    async void RetryClicked() => await Start();

    void OpenLauncher()
    {
        try
        {
            MinecraftLauncher.Start();
            Close();
        }
        catch (Exception e)
        {
            ShowError(e is InstallException ? e.Message : $"Couldn't start the launcher: {e.Message}",
                e is InstallException { LauncherMissing: true });
        }
    }

    static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // Thin dark progress bar; with no fraction it shows a sliding block instead.
    class ProgressTrack : Control
    {
        readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
        double? fraction;
        int slide;

        public ProgressTrack()
        {
            DoubleBuffered = true;
            timer.Tick += (_, _) =>
            {
                slide = (slide + 6) % (Width + 120);
                Invalidate();
            };
        }

        public double? Fraction
        {
            get => fraction;
            set
            {
                fraction = value;
                timer.Enabled = value == null && Visible;
                Invalidate();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            timer.Enabled = fraction == null && Visible;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Panel);
            using var brush = new SolidBrush(Accent);
            if (fraction is double value)
            {
                e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * Math.Clamp(value, 0, 1)), Height);
            }
            else
            {
                e.Graphics.SetClip(new Rectangle(0, 0, Width, Height));
                e.Graphics.FillRectangle(brush, slide - 120, 0, 120, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
