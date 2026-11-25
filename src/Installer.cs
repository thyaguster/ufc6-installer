using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class InstallerSettings
{
    public long XboxIconsBytes { get; set; }
    public string XboxIconsSha256 { get; set; }
}

internal static class Program
{
    internal static readonly Assembly Assembly = Assembly.GetExecutingAssembly();
    internal static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
    internal static Package Package;
    internal static PackageInput PackageSource;
    internal static string PackageError;
    private static string ResourceText(string name)
    { using (var reader = new StreamReader(Assembly.GetManifestResourceStream(name))) return reader.ReadToEnd(); }
    internal static void LoadPackage()
    {
        Package = new Package { Title = "EA SPORTS UFC 6" }; PackageError = null;
        try
        {
            var settings = new JavaScriptSerializer().Deserialize<InstallerSettings>(ResourceText("installer.json"));
            PackageSource = PackageInput.Open(BaseDirectory);
            using (var input = PackageSource.OpenRead()) Package = SignedPackage.Read(input, ResourceText("package-public-key.xml"));
            Package.XboxIconsBytes = settings.XboxIconsBytes; Package.XboxIconsSha256 = settings.XboxIconsSha256;
        }
        catch (Exception ex) { PackageSource = null; PackageError = ex is FileNotFoundException || ex is DirectoryNotFoundException ? "Place data.bin, or data.bin.header with every numbered part, beside setup.exe to continue." : ex.Message; }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        LoadPackage();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // The same extraction engine can be exercised without opening a window.
        if (args.Length >= 2 && args[0] == "--extract")
        {
            bool addon = true, xbox = false;
            for (int i = 2; i < args.Length; i++)
                if (args[i] == "--without-alter-egos") addon = false;
                else if (args[i] == "--xbox-icons") xbox = true;
                else if (args[i].StartsWith("--mode=", StringComparison.Ordinal))
                {
                    InstallationMode mode;
                    if (!Enum.TryParse<InstallationMode>(args[i].Substring(7), true, out mode) || !Enum.IsDefined(typeof(InstallationMode), mode)) return 1;
                    InstallationPerformance.Mode = mode;
                }
                else return 1;
            try { Install(args[1], CancellationToken.None, delegate { }, addon, xbox); return 0; }
            catch (Exception ex) { InstallLog.Failure(ex); return 1; }
        }
        if (args.Length == 2 && args[0] == "--inspect-package")
        {
            if (PackageError != null) { InstallLog.Failure(new IOException(PackageError)); return 1; }
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new { Package.Title, Package.GameVersion,
                Package.ArchiveBytes, Package.PayloadBytes, Package.Sha256, Package.FileCount, Package.UnpackedBytes,
                Package.AlterEgosArchiveBytes, Package.AlterEgosFileDelta, Package.AlterEgosInstalledDeltaBytes })); return 0;
        }
        using (var form = new InstallerForm())
        {
            if (args.Length >= 2 && args[0] == "--preview")
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                if (args.Length >= 5)
                {
                    float scale = Single.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                    float actual;
                    using (var graphics = form.CreateGraphics()) actual = graphics.DpiX / 96f;
                    form.ScalePreview(scale / actual);
                    form.ConfigureViewport(new Size(Int32.Parse(args[3]), Int32.Parse(args[4])), scale);
                    int previewPage;
                    if (args.Length >= 7 && Int32.TryParse(args[6], out previewPage)) form.ShowPage(previewPage);
                    if (Array.IndexOf(args, "offline") >= 0)
                        form.SetOptionalToolsOnline(false, "No internet connection to GitHub · Optional tools are unavailable");
                    form.AutoScrollPosition = args.Length >= 6 && args[5] == "bottom" ? new Point(0, form.DisplayRectangle.Height) : Point.Empty;
                    Application.DoEvents();
                }
                using (var bitmap = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[1]); }
                return 0;
            }
            Application.Run(form);
        }
        return 0;
    }

    internal static string Size(long bytes)
    {
        if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("N2") + " GiB";
        if (bytes >= 1048576) return (bytes / 1048576.0).ToString("N1") + " MiB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("N1") + " KiB";
        return bytes.ToString("N0") + " B";
    }

    internal static long XboxOverhead { get { return Package.XboxIconsBytes + 20L * 1024 * 1024; } }
    internal static long RequiredSpace(bool addon, bool xbox = false)
    { return Package.UnpackedBytes + 256L * 1024 * 1024 + (addon ? Package.AlterEgosUnpackedBytes + Math.Max(0, Package.AlterEgosInstalledDeltaBytes) : 0) + (xbox ? XboxOverhead : 0); }

    internal static void Install(string requestedDestination, CancellationToken cancellation, Action<int, string> progress, bool installAlterEgos = true, bool xboxIcons = false)
    {
        InstallLog.Begin("Game installation");
        if (PackageError != null) throw new IOException(PackageError);
        InstallLog.Write("Game version=" + Package.GameVersion + "; destination=" + requestedDestination);
        InstallLog.Write("Options: AlterEgos=" + installAlterEgos + "; ButtonIcons=" + (xboxIcons ? "Xbox" : "PlayStation") + "; DownloadPortheon=" + InstallLog.PortheonRequested + "; DownloadOverlay=" + InstallLog.OverlayRequested);
        InstallLog.Write("Installation mode=" + InstallationPerformance.Mode);
        InstallLog.Write("Source package=" + (PackageSource != null && PackageSource.IsSplit ? "numbered data.bin parts" : "data.bin") + "; ExpectedSHA256=" + Package.Sha256);
        bool addon = installAlterEgos && Package.AlterEgosArchiveBytes > 0;
        if (xboxIcons && Package.XboxIconsBytes == 0) throw new IOException("Xbox button icons are not included in this package.");
        if (PackageSource == null) throw new IOException("Place data.bin, or data.bin.header with every numbered part, beside setup.exe, then try again.");
        if (PackageSource.Length != Package.ArchiveBytes)
            throw new IOException("The data.bin package has the wrong size. Restore the supplied file or every numbered part.");
        string destination = Path.GetFullPath(requestedDestination).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Path.GetDirectoryName(destination);
        if (String.IsNullOrEmpty(parent) || destination == Path.GetPathRoot(destination).TrimEnd('\\'))
            throw new IOException("Choose a game folder, rather than a drive root.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Choose a new folder. The destination already exists.");
        var drive = new DriveInfo(Path.GetPathRoot(destination));
        if (drive.AvailableFreeSpace < RequiredSpace(addon, xboxIcons))
            throw new IOException("There is not enough free space. Allow " + Size(RequiredSpace(addon, xboxIcons)) + ".");

        using (var sha = SHA256.Create())
        {
            string archive = PackageSource.ArchivePath;
            progress(0, "Checking game data...");
            // Keep the verified archive locked against replacement until extraction ends.
            using (var source = PackageSource.OpenRead())
            {
            Package current = SignedPackage.Read(source, ResourceText("package-public-key.xml"));
            if (current.ManifestSha256 != Package.ManifestSha256)
                throw new IOException("data.bin changed after setup opened. Restart the installer to review the new package.");
            source.Position = current.PayloadOffset;
            byte[] buffer = new byte[4 * 1024 * 1024]; long read = 0; int count;
            var pacer = new InstallationPerformance.VerificationPacer();
            while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, count, buffer, 0); read += count;
                pacer.Pace(count, cancellation);
                progress((int)(read * 10 / Math.Max(1, Package.PayloadBytes)), "Checking game data...");
            }
            sha.TransformFinalBlock(buffer, 0, 0);
            string actual = BitConverter.ToString(sha.Hash).Replace("-", "");
            if (!String.Equals(actual, Package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("data.bin failed its integrity check. Restore the original file and try again.");
            InstallLog.Write("Archive SHA-256 verified.");

            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".ufc6-install-" + Guid.NewGuid().ToString("N"));
            string tools = Path.Combine(Path.GetTempPath(), "ufc6-setup-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(staging); Directory.CreateDirectory(tools);
                foreach (string file in new[] { "7z.exe", "7z.dll" })
                    using (var input = Assembly.GetManifestResourceStream(file))
                    using (var output = File.Create(Path.Combine(tools, file))) input.CopyTo(output);
                progress(10, "Installing game files...");
                Extract(tools, archive, staging, PackageSource.IsSplit ? "" : "7z", cancellation,
                    delegate(int percent) { progress(Math.Min(addon ? 89 : 99, 10 + percent * (addon ? 79 : 89) / 100), "Installing game files..."); }, null, true);
                InstallLog.Write("Base game extracted; archive entry CRC checks passed.");
                if (addon)
                {
                    cancellation.ThrowIfCancellationRequested();
                    progress(90, "Installing Alter Egos...");
                    if (Package.PayloadVersion != 2 || Package.AlterEgosArchivePath != "__ufc6_installer__\\alter-egos.rar")
                        throw new IOException("The optional payload metadata is invalid.");
                    Extract(tools, archive, tools, PackageSource.IsSplit ? "" : "7z", cancellation, delegate { }, Package.AlterEgosArchivePath);
                    string addonArchive = Path.Combine(tools, Package.AlterEgosArchivePath);
                    using (var input = File.OpenRead(addonArchive))
                    using (var hash = SHA256.Create())
                        if (!String.Equals(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""), Package.AlterEgosSha256, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("The included Alter Egos archive failed its integrity check.");
                    string addonStaging = Path.Combine(staging, ".alter-egos-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(addonStaging);
                    Extract(tools, addonArchive, addonStaging, "", cancellation,
                        delegate(int percent) { progress(Math.Min(97, 90 + percent * 7 / 100), "Installing Alter Egos..."); });
                    string root = Path.Combine(addonStaging, "PPSA23566");
                    if (!Directory.Exists(root)) throw new IOException("The Alter Egos archive has an unexpected folder structure.");
                    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string target = Path.Combine(staging, file.Substring(root.Length + 1));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(file, target, true);
                    }
                    Directory.Delete(addonStaging, true);
                    progress(99, "Installing Alter Egos...");
                    InstallLog.Write("Alter Egos overlay applied.");
                }
                if (xboxIcons)
                {
                    progress(99, "Installing Xbox button icons...");
                    InstallXboxIcons(tools, staging, cancellation);
                    InstallLog.Write("Xbox icons installed and verified.");
                }
                cancellation.ThrowIfCancellationRequested();
                // Rename only after successful extraction; never merge into existing data.
                Directory.Move(staging, destination);
                InstallLog.Write("SUCCESS: game installation finalized at " + destination);
                progress(100, "Installation complete.");
            }
            finally
            {
                // Only directories created with this invocation's random names are removed.
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (Directory.Exists(tools)) Directory.Delete(tools, true);
                InstallLog.Write("Temporary extraction/tool directories cleaned.");
            }
            }
        }
    }
    internal static void InstallXboxIcons(string tools, string destination, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string modFolder = Path.Combine(destination, "Mods", "Xbox Icons");
        Directory.CreateDirectory(modFolder);
        string executable = Path.Combine(modFolder, "UFC6-Xbox-Icons.exe");
        if (File.Exists(executable)) throw new IOException("The Xbox Icons tool already exists in this package.");
        using (var input = Assembly.GetManifestResourceStream("xbox-icons.exe"))
        using (var output = File.Create(executable)) input.CopyTo(output);
        using (var input = File.OpenRead(executable))
        using (var sha = SHA256.Create())
            if (!String.Equals(BitConverter.ToString(sha.ComputeHash(input)).Replace("-", ""), Package.XboxIconsSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The Xbox Icons patcher failed its integrity check.");
        string report = Path.Combine(tools, "xbox-icons-report.json");
        var info = new ProcessStartInfo(executable, "--install --game-dir \"" + destination + "\" --report \"" + report + "\"");
        info.UseShellExecute = false; info.CreateNoWindow = true;
        using (var process = Process.Start(info))
        using (var budget = InstallationPerformance.Attach(process))
        {
            // Allow the patcher's own write/rollback transaction to finish before cancellation.
            process.WaitForExit(); cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(report)) throw new IOException("The Xbox Icons patcher did not return a result.");
            var result = new JavaScriptSerializer().Deserialize<IconResult>(File.ReadAllText(report));
            if (process.ExitCode != 0 || result == null || !result.success)
                throw new IOException("Xbox button icons could not be installed. " + (result == null || result.messages == null ? "" : String.Join("\n", result.messages)));
        }
    }
    private sealed class IconResult
    {
        public bool success { get; set; }
        public string[] messages { get; set; }
    }
    private static void Extract(string tools, string archive, string destination, string format, CancellationToken cancellation, Action<int> progress, string entry = null, bool excludePayload = false)
    {
        cancellation.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(Path.Combine(tools, "7z.exe"),
            "x " + (String.IsNullOrEmpty(format) ? "" : "-t" + format + " ") +
            (entry != null ? "-i!\"" + entry + "\" " : excludePayload ? "-x!__ufc6_installer__ -x!__ufc6_installer__\\* " : "") +
            "-y -aoa -bb0 -bsp1 -bso1 -bse2 -o\"" + destination + "\" -- \"" + archive + "\"");
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        using (var process = Process.Start(info))
        using (var budget = InstallationPerformance.Attach(process))
        using (cancellation.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }))
        {
            Task<string> errors = process.StandardError.ReadToEndAsync();
            char[] chars = new char[4096]; var pending = new StringBuilder(); int length;
            while ((length = process.StandardOutput.Read(chars, 0, chars.Length)) > 0)
            {
                pending.Append(chars, 0, length);
                var matches = Regex.Matches(pending.ToString(), @"(\d{1,3})%");
                if (matches.Count > 0) progress(Int32.Parse(matches[matches.Count - 1].Groups[1].Value));
                if (pending.Length > 4096) pending.Remove(0, pending.Length - 16);
            }
            process.WaitForExit(); cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException("Extraction failed: " + errors.Result.Trim());
        }
    }

}

internal sealed class HeroPanel : Panel
{
    internal HeroPanel() { DoubleBuffered = true; BackColor = Color.FromArgb(14, 15, 18); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(Color.FromArgb(24, 25, 30)))
            e.Graphics.FillPolygon(brush, new[] { new Point(240, 100), new Point(240, 450), new Point(0, 570), new Point(0, 450) });
        using (var pen = new Pen(Color.FromArgb(40, 41, 47), 1))
            for (int y = 250; y < 610; y += 28) e.Graphics.DrawLine(pen, 0, y, 240, y - 125);
        using (var brush = new SolidBrush(Color.FromArgb(241, 49, 65)))
            e.Graphics.FillRectangle(brush, 0, 0, 5, Height);
    }
}

internal sealed class InstallProgress : Control
{
    internal int Value;
    internal bool Finished;
    internal InstallProgress() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        using (var track = new SolidBrush(Color.FromArgb(47, 49, 57))) e.Graphics.FillRectangle(track, ClientRectangle);
        using (var fill = new SolidBrush(Finished ? Color.FromArgb(95, 209, 156) : Color.FromArgb(241, 49, 65)))
            e.Graphics.FillRectangle(fill, 0, 0, Width * Math.Max(0, Math.Min(100, Value)) / 100, Height);
    }
}

internal static class RoundedBox
{
    internal static GraphicsPath Path(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
        if (radius <= 0) { path.AddRectangle(bounds); return path; }
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal sealed class SoftButton : Button
{
    private bool hover, pressed;
    internal SoftButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor); }
    protected override void OnPaint(PaintEventArgs e)
    {
        // ButtonBase can skip background painting during native state changes.
        // Repaint the entire rectangle before drawing the rounded surface.
        var g = e.Graphics; g.Clear(Parent == null ? BackColor : Parent.BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = g.DpiX / 96f * Font.Size / 10f;
        Color fill = !Enabled ? Color.FromArgb(45, 48, 57) : pressed ? ControlPaint.Dark(BackColor, 0.12f) : hover ? FlatAppearance.MouseOverBackColor : BackColor;
        using (var path = RoundedBox.Path(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * scale))
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (FlatAppearance.BorderSize > 0)
                using (var pen = new Pen(FlatAppearance.BorderColor)) g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(156, 160, 173),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5), Color.White, fill);
    }
}

internal sealed class PillSwitch : CheckBox
{
    internal PillSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false; AccessibleRole = AccessibleRole.CheckButton; Cursor = Cursors.Hand;
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent == null ? BackColor : Parent.BackColor);
        float scale = Height / 28f;
        var track = new RectangleF(Width - 44 * scale, 3 * scale, 40 * scale, 22 * scale);
        Color fill = !Enabled ? Color.FromArgb(63, 66, 77) : Checked ? Color.FromArgb(241, 49, 65) : Color.FromArgb(72, 76, 90);
        using (var path = RoundedBox.Path(track, track.Height / 2))
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var brush = new SolidBrush(Enabled ? Color.White : Color.FromArgb(156, 160, 173)))
            g.FillEllipse(brush, Checked ? track.Right - 19 * scale : track.Left + 3 * scale, track.Top + 3 * scale, 16 * scale, 16 * scale);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Math.Max(0, (int)track.Left - 12), Height), Enabled ? ForeColor : Color.FromArgb(156, 160, 173),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), Color.White, Parent == null ? BackColor : Parent.BackColor);
    }
}

internal sealed class ModePicker : Control
{
    private readonly string[] choices = { "Fast", "Balanced", "Quiet" };
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private int selected = 1;
    private bool hover;
    public event EventHandler SelectedIndexChanged;
    public int SelectedIndex
    {
        get { return selected; }
        set
        {
            if (value < 0 || value >= choices.Length) throw new ArgumentOutOfRangeException("value");
            if (selected == value) return;
            selected = value; Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
    }
    public string SelectedItem { get { return choices[selected]; } }
    internal ModePicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox;
        menu.Renderer = new ModeMenuRenderer(); menu.ShowImageMargin = false; menu.ShowCheckMargin = true;
        menu.BackColor = Color.FromArgb(27, 29, 36); menu.ForeColor = Color.White;
        menu.Closed += delegate { Invalidate(); };
        menu.SizeChanged += delegate
        {
            if (menu.Width < 2 || menu.Height < 2) return;
            using (var path = RoundedBox.Path(new RectangleF(0, 0, menu.Width, menu.Height), 8 * Height / 28f))
            { var old = menu.Region; menu.Region = new Region(path); if (old != null) old.Dispose(); }
        };
    }
    private void ShowOptions()
    {
        if (!Enabled) return;
        Focus();
        if (menu.Visible) { menu.Close(); return; }
        menu.Items.Clear(); menu.Font = Font;
        float scale = Height / 28f;
        menu.Padding = new Padding((int)(4 * scale));
        menu.ImageScalingSize = new Size((int)(16 * scale), (int)(16 * scale));
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(choices[i]) { Checked = selected == i, AutoSize = false,
                Size = new Size(Math.Max(Width, (int)(140 * scale)), (int)(32 * scale)), ForeColor = Color.White };
            item.Click += delegate { SelectedIndex = index; };
            menu.Items.Add(item);
        }
        menu.Show(this, new Point(0, Height + (int)(4 * scale))); Invalidate();
    }
    protected override void OnClick(EventArgs e) { base.OnClick(e); ShowOptions(); }
    protected override bool IsInputKey(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || key == Keys.F4 || key == Keys.Enter || key == Keys.Space || base.IsInputKey(keyData);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || (e.Alt && e.KeyCode == Keys.Down)) ShowOptions();
        else if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(choices.Length - 1, selected + 1);
        else if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, selected - 1);
        else if (e.KeyCode == Keys.Home) SelectedIndex = 0;
        else if (e.KeyCode == Keys.End) SelectedIndex = choices.Length - 1;
        else { base.OnKeyDown(e); return; }
        e.Handled = e.SuppressKeyPress = true;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) menu.Close(); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? Color.FromArgb(33, 35, 42) : Parent.BackColor); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float scale = Height / 28f;
        using (var path = RoundedBox.Path(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * scale))
        {
            using (var brush = new SolidBrush(hover || menu.Visible ? Color.FromArgb(39, 42, 51) : Color.FromArgb(27, 29, 36))) g.FillPath(brush, path);
            using (var pen = new Pen(Focused || menu.Visible ? Color.FromArgb(241, 49, 65) : Color.FromArgb(65, 69, 81))) g.DrawPath(pen, path);
        }
        Color ink = Enabled ? Color.White : Color.FromArgb(156, 160, 173);
        TextRenderer.DrawText(g, SelectedItem, Font, new Rectangle((int)(12 * scale), 0, Math.Max(1, Width - (int)(44 * scale)), Height), ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        using (var pen = new Pen(ink, 1.5f * scale))
        { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, new[] { new PointF(Width - 25 * scale, Height / 2f - 2 * scale), new PointF(Width - 20 * scale, Height / 2f + 3 * scale), new PointF(Width - 15 * scale, Height / 2f - 2 * scale) }); }
    }
    protected override AccessibleObject CreateAccessibilityInstance() { return new ModeAccess(this); }
    private sealed class ModeAccess : ControlAccessibleObject
    {
        private readonly ModePicker owner;
        internal ModeAccess(ModePicker owner) : base(owner) { this.owner = owner; }
        public override string Value { get { return owner.SelectedItem; } set { } }
        public override string DefaultAction { get { return "Open options"; } }
        public override void DoDefaultAction() { owner.ShowOptions(); }
        public override AccessibleStates State { get { return base.State | (owner.menu.Visible ? AccessibleStates.Expanded : AccessibleStates.Collapsed); } }
    }
    private sealed class ModeMenuRenderer : ToolStripProfessionalRenderer
    {
        internal ModeMenuRenderer() : base(new ModeColors()) { RoundedEdges = false; }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = r.Width / 16f;
            using (var pen = new Pen(Color.FromArgb(241, 49, 65), 2 * scale))
                e.Graphics.DrawLines(pen, new[] { new PointF(r.Left + 2 * scale, r.Top + r.Height / 2f), new PointF(r.Left + r.Width / 2f - scale, r.Bottom - 3 * scale), new PointF(r.Right - 2 * scale, r.Top + 3 * scale) });
        }
    }
    private sealed class ModeColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(27, 29, 36); } }
        public override Color MenuBorder { get { return Color.FromArgb(65, 69, 81); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(241, 49, 65); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(54, 33, 42); } }
        public override Color MenuItemSelectedGradientBegin { get { return MenuItemSelected; } }
        public override Color MenuItemSelectedGradientEnd { get { return MenuItemSelected; } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color CheckBackground { get { return ToolStripDropDownBackground; } }
        public override Color CheckSelectedBackground { get { return MenuItemSelected; } }
        public override Color CheckPressedBackground { get { return MenuItemSelected; } }
    }
    protected override void Dispose(bool disposing) { if (disposing) menu.Dispose(); base.Dispose(disposing); }
}

internal sealed class StepIndicator : Control
{
    internal int CurrentStep;
    internal StepIndicator()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.StaticText;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Width / 600f; var state = g.Save(); g.ScaleTransform(scale, scale);
        Color accent = Color.FromArgb(241, 49, 65), muted = Color.FromArgb(156, 160, 173), line = Color.FromArgb(65, 69, 81);
        for (int i = 0; i < 2; i++)
            using (var pen = new Pen(i < CurrentStep ? accent : line, 1)) g.DrawLine(pen, 112 + i * 200, 11, 288 + i * 200, 11);
        using (var numberFont = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var labelFont = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        for (int i = 0; i < 3; i++)
        {
            float x = 100 + i * 200;
            using (var brush = new SolidBrush(i == CurrentStep ? accent : Parent.BackColor)) g.FillEllipse(brush, x - 10, 1, 20, 20);
            using (var pen = new Pen(i <= CurrentStep ? accent : line, 1)) g.DrawEllipse(pen, x - 10, 1, 20, 20);
            using (var brush = new SolidBrush(i == CurrentStep ? Color.White : muted))
            {
                using (var glyph = new GraphicsPath())
                using (var transform = new Matrix())
                {
                    glyph.AddString((i + 1).ToString(), numberFont.FontFamily, (int)numberFont.Style,
                        numberFont.Size, PointF.Empty, StringFormat.GenericTypographic);
                    RectangleF bounds = glyph.GetBounds();
                    transform.Translate(x - bounds.Left - bounds.Width / 2, 11 - bounds.Top - bounds.Height / 2);
                    glyph.Transform(transform); g.FillPath(brush, glyph);
                }
                g.DrawString(new[] { "GAME SETUP", "OPTIONAL TOOLS", "INSTALL" }[i], labelFont, brush, new RectangleF(x - 98, 23, 196, 15), format);
            }
        }
        g.Restore(state);
    }
}

internal sealed class InfoCard : Panel
{
    internal Color BorderColor = Color.FromArgb(55, 59, 70);
    internal float BorderThickness = 1;
    internal InfoCard() { DoubleBuffered = true; BackColor = Color.FromArgb(33, 35, 42); }
    private GraphicsPath Outline(Graphics g)
    {
        float inset = BorderThickness / 2;
        return RoundedBox.Path(new RectangleF(inset, inset, Width - BorderThickness, Height - BorderThickness), 8 * g.DpiX / 96f * Font.Size / 10f);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent == null ? Color.FromArgb(24, 25, 30) : Parent.BackColor);
        using (var path = Outline(e.Graphics))
        using (var brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Outline(e.Graphics))
        using (var pen = new Pen(BorderColor, BorderThickness)) e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ButtonIconChoice : RadioButton
{
    internal bool Xbox;
    private bool hover;
    internal ButtonIconChoice()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Appearance = Appearance.Button; AutoSize = false; AccessibleRole = AccessibleRole.RadioButton;
        Cursor = Cursors.Hand;
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var state = g.Save(); float scale = Width / 278f; g.ScaleTransform(scale, scale);
        float logicalHeight = Height / scale;
        Color accent = Color.FromArgb(241, 49, 65), muted = Color.FromArgb(156, 160, 173);
        Color surface = Checked && Enabled ? Color.FromArgb(54, 33, 42) : hover && Enabled ? Color.FromArgb(43, 46, 55) : Color.FromArgb(27, 29, 36);
        g.Clear(Parent == null ? Color.FromArgb(33, 35, 42) : Parent.BackColor);
        using (var outline = RoundedBox.Path(new RectangleF(1, 1, 276, logicalHeight - 2), 8))
        {
            using (var brush = new SolidBrush(surface)) g.FillPath(brush, outline);
            using (var pen = new Pen(Checked && Enabled ? accent : Color.FromArgb(65, 69, 81), Checked ? 1.5f : 1f)) g.DrawPath(pen, outline);
        }
        using (var pen = new Pen(Checked && Enabled ? accent : muted, 1.4f)) g.DrawEllipse(pen, 15, 12, 11, 11);
        if (Checked) using (var brush = new SolidBrush(Enabled ? accent : muted)) g.FillEllipse(brush, 18, 15, 5, 5);
        using (var font = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(Enabled ? Color.White : muted)) g.DrawString(Text, font, brush, 33, 8);
        using (var font = new Font("Segoe UI", 9, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(muted)) g.DrawString(Xbox ? "MOD" : "DEFAULT", font, brush, 219, 13);
        Color[] colors = Xbox ? new[] { Color.FromArgb(113, 207, 133), Color.FromArgb(244, 117, 125), Color.FromArgb(117, 177, 247), Color.FromArgb(244, 205, 100) }
            : new[] { Color.FromArgb(99, 211, 177), Color.FromArgb(242, 129, 154), Color.FromArgb(124, 177, 247), Color.FromArgb(209, 151, 222) };
        for (int i = 0; i < 4; i++)
        {
            float x = 16 + i * 32, y = logicalHeight <= 64 ? 31 : 39; Color color = Enabled ? colors[i] : muted;
            // Optical sizing: PS shapes fill their bounds more strongly than
            // lettered circles, so inset them while preserving the same centers.
            float iconSize = Xbox ? 20 : 16;
            float inset = (20 - iconSize) / 2;
            var icon = new RectangleF(x + 0.5f + inset, y + 0.5f + inset, iconSize, iconSize);
            using (var pen = new Pen(color, 1.7f))
            {
                pen.LineJoin = LineJoin.Round; pen.StartCap = pen.EndCap = LineCap.Round;
                if (Xbox)
                {
                    g.DrawEllipse(pen, icon);
                    // Center the actual glyph outline, rather than the font's
                    // padded text rectangle, so every letter sits squarely.
                    using (var family = new FontFamily("Arial"))
                    using (var glyph = new GraphicsPath())
                    using (var brush = new SolidBrush(color))
                    using (var transform = new Matrix())
                    {
                        glyph.AddString("ABXY"[i].ToString(), family, (int)FontStyle.Bold, 14f, PointF.Empty, StringFormat.GenericTypographic);
                        RectangleF bounds = glyph.GetBounds();
                        transform.Translate(icon.Left + icon.Width / 2 - bounds.Left - bounds.Width / 2, icon.Top + icon.Height / 2 - bounds.Top - bounds.Height / 2);
                        glyph.Transform(transform); g.FillPath(brush, glyph);
                    }
                }
                else if (i == 0) g.DrawPolygon(pen, new[] { new PointF(icon.Left + icon.Width / 2, icon.Top), new PointF(icon.Right, icon.Bottom), new PointF(icon.Left, icon.Bottom) });
                else if (i == 1) g.DrawEllipse(pen, icon);
                else if (i == 2) { g.DrawLine(pen, icon.Left, icon.Top, icon.Right, icon.Bottom); g.DrawLine(pen, icon.Right, icon.Top, icon.Left, icon.Bottom); }
                else g.DrawRectangle(pen, icon.X, icon.Y, icon.Width, icon.Height);
            }
        }
        using (var font = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(muted)) g.DrawString(Xbox ? "Applies mod" : "Original icons", font, brush, 158, logicalHeight <= 64 ? 35 : 43);
        g.Restore(state);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 8, Height - 8), Color.White, surface);
    }
}

internal sealed class InstallerForm : Form
{
    private static readonly Color Background = Color.FromArgb(24, 25, 30);
    private static readonly Color Muted = Color.FromArgb(156, 160, 173);
    private static readonly Color Accent = Color.FromArgb(241, 49, 65);
    private static readonly Color Green = Color.FromArgb(95, 209, 156);
    private readonly TextBox folder = new TextBox();
    private readonly Button browse = new SoftButton();
    private readonly Button install = new SoftButton();
    private readonly Button cancel = new SoftButton();
    private readonly Button back = new SoftButton();
    private readonly Panel[] pages = { new Panel(), new Panel(), new Panel() };
    private readonly StepIndicator steps = new StepIndicator();
    private readonly Label pageTitle = new Label();
    private Label summaryDestination, summaryAlterEgos, summaryIcons, summaryTools, summaryTotals;
    private int currentPage;
    private readonly InstallProgress progress = new InstallProgress();
    private readonly Label status = new Label();
    private readonly Label percent = new Label();
    private readonly Label space = new Label();
    private readonly Label timing = new Label();
    private readonly ModePicker performanceMode = new ModePicker();
    private readonly Label modeHint = new Label();
    private readonly Label description = new Label();
    private readonly CheckBox alterEgos = new PillSwitch();
    private readonly ButtonIconChoice playStationIcons = new ButtonIconChoice();
    private readonly ButtonIconChoice xboxIcons = new ButtonIconChoice { Xbox = true };
    private readonly CheckBox installPortheon = new PillSwitch();
    private readonly CheckBox installOverlay = new PillSwitch();
    private readonly Button overlayGithub = new SoftButton();
    private readonly Button portheonGithub = new SoftButton();
    private readonly Button[] toolInstructions = { new SoftButton(), new SoftButton() };
    private readonly Label optionalToolsStatus = new Label();
    private readonly PictureBox overlayLogo = new PictureBox();
    private readonly Button playNow = new SoftButton();
    private readonly PictureBox portheonLogo = new PictureBox();
    private InfoCard overlayPanel;
    private InfoCard launcherPanel;
    private Label overlayTitle, overlayDescription, overlayNote, launcherTitle, launcherDescription, launcherNote;
    private PortheonDownloader.ToolSlot[] toolSlots = new PortheonDownloader.ToolSlot[2];
    private HeroPanel hero;
    private bool condensed;
    private string portheonExecutable;
    private bool launcherPending;
    private bool overlayPending;
    private bool optionalToolsChecked;
    private bool optionalToolsOnline = true;
    private CancellationTokenSource optionalToolsCheck;
    private Label installedSize;
    private Label fileCount;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly Stopwatch elapsed = new Stopwatch();
    private readonly Stopwatch phaseElapsed = new Stopwatch();
    private CancellationTokenSource cancellation;
    private bool running;
    private bool complete;
    private bool attempted;
    private bool existingGameDetected;
    private int phase = -1;
    private double phasePercent;
    private string installedDestination;

    private Label LabelAt(Control parent, string text, int x, int y, int width, int height, float size, Color color, FontStyle style)
    {
        var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
            Font = new Font("Segoe UI", size, style), ForeColor = color, BackColor = Color.Transparent };
        parent.Controls.Add(label); return label;
    }
    private void ButtonAt(Button button, string text, int x, int y, int width, bool primary)
    {
        button.Text = text; button.SetBounds(x, y, width, 42); button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Accent : Color.FromArgb(36, 38, 45); button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(64, 67, 78);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 68, 84) : Color.FromArgb(49, 52, 61);
        button.Cursor = Cursors.Hand; button.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        button.EnabledChanged += delegate
        {
            button.BackColor = !button.Enabled ? Color.FromArgb(45, 48, 57) : primary ? Accent : Color.FromArgb(36, 38, 45);
            button.ForeColor = button.Enabled ? Color.White : Muted;
        };
        Controls.Add(button);
    }
    private Label Stat(string caption, string value, int x)
    {
        var panel = new InfoCard { Location = new Point(x, 136), Size = new Size(192, 72) };
        Controls.Add(panel);
        LabelAt(panel, caption, 16, 9, 170, 20, 9, Muted, FontStyle.Regular);
        return LabelAt(panel, value, 14, 28, 174, 39, 20, Color.White, FontStyle.Bold);
    }

    internal InstallerForm()
    {
        Text = Program.Package.Title + " — Installer";
        Icon = Icon.ExtractAssociatedIcon(Program.Assembly.Location);
        ClientSize = new Size(920, Math.Min(700, Screen.PrimaryScreen.WorkingArea.Height - 64)); FormBorderStyle = FormBorderStyle.FixedSingle;
        AutoScroll = true; AutoScrollMinSize = new Size(900, 700);
        MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); BackColor = Background; ForeColor = Color.White;
        AutoScaleMode = AutoScaleMode.None;
        hero = new HeroPanel { Location = Point.Empty, Size = new Size(240, 700) }; Controls.Add(hero);
        LabelAt(hero, "EA SPORTS", 28, 37, 190, 30, 15, Color.White, FontStyle.Bold);
        LabelAt(hero, "UFC", 17, 104, 220, 120, 58, Color.White, FontStyle.Bold | FontStyle.Italic);
        LabelAt(hero, "6", 9, 191, 222, 298, 158, Accent, FontStyle.Bold);
        var rule = new Panel { BackColor = Color.FromArgb(62, 64, 71), Location = new Point(28, 489), Size = new Size(184, 1) }; hero.Controls.Add(rule);
        LabelAt(hero, "EA SPORTS UFC 6", 28, 510, 198, 28, 12, Color.White, FontStyle.Bold);
        LabelAt(hero, "GAME FILE INSTALLER", 28, 544, 200, 24, 8, Muted, FontStyle.Regular);
        string version = String.IsNullOrWhiteSpace(Program.Package.GameVersion) ? "" : "Version " + Program.Package.GameVersion;
        LabelAt(hero, version, 28, 573, 195, 24, 9, Muted, FontStyle.Regular);
        LabelAt(this, "INSTALLATION  /  GAME PACKAGE", 280, 25, 330, 24, 9, Accent, FontStyle.Bold);
        var versionBadge = LabelAt(this,
            String.IsNullOrWhiteSpace(Program.Package.GameVersion) ? "GAME VERSION UNAVAILABLE" : "GAME VERSION  " + Program.Package.GameVersion,
            626, 21, 254, 28, 10, Color.White, FontStyle.Bold);
        versionBadge.BackColor = Color.FromArgb(89, 31, 43);
        versionBadge.TextAlign = ContentAlignment.MiddleCenter;
        versionBadge.AccessibleName = "Game version being installed";
        pageTitle.SetBounds(276, 55, 615, 48); pageTitle.Font = new Font("Segoe UI", 24, FontStyle.Bold);
        pageTitle.ForeColor = Color.White; Controls.Add(pageTitle);
        description.SetBounds(280, 107, 600, 25); description.ForeColor = Muted;
        description.Text = "Choose a destination to install your game files."; Controls.Add(description);
        Stat("PACKAGE SIZE", Program.Size(Program.Package.ArchiveBytes + Program.Package.XboxIconsBytes), 280);
        installedSize = Stat("INSTALLED SIZE", Program.Size(Program.Package.UnpackedBytes), 484);
        fileCount = Stat("FILES TO INSTALL", Program.Package.FileCount.ToString("N0"), 688);
        var destinationCard = new InfoCard { Location = new Point(280, 222), Size = new Size(600, 124) }; Controls.Add(destinationCard);
        LabelAt(destinationCard, "Destination folder", 16, 12, 560, 26, 11, Color.White, FontStyle.Bold);
        var input = new Panel { Location = new Point(16, 44), Size = new Size(443, 36), BackColor = Color.FromArgb(25, 27, 34), Padding = new Padding(12, 7, 12, 6) };
        folder.BorderStyle = BorderStyle.None; folder.Dock = DockStyle.Fill; folder.BackColor = input.BackColor;
        folder.ForeColor = Color.White; input.Controls.Add(folder); destinationCard.Controls.Add(input);
        folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "EA SPORTS UFC 6");
        folder.AccessibleName = "Destination folder";
        ButtonAt(browse, "Browse…", 471, 44, 113, false); browse.Height = 36; destinationCard.Controls.Add(browse);
        browse.Click += delegate
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the parent folder for EA SPORTS UFC 6" })
                if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = Path.Combine(dialog.SelectedPath, "EA SPORTS UFC 6");
        };
        space.SetBounds(16, 88, 568, 30); space.Font = new Font("Segoe UI", 9); destinationCard.Controls.Add(space);
        var optionPanel = new InfoCard { Location = new Point(280, 358), Size = new Size(600, 70) }; Controls.Add(optionPanel);
        alterEgos.Text = "Install Alter Egos"; alterEgos.SetBounds(16, 12, 568, 28);
        alterEgos.Font = new Font("Segoe UI", 10, FontStyle.Bold); alterEgos.ForeColor = Color.White;
        alterEgos.Checked = Program.Package.AlterEgosArchiveBytes > 0; alterEgos.Enabled = alterEgos.Checked;
        alterEgos.Cursor = Cursors.Hand; optionPanel.Controls.Add(alterEgos);
        LabelAt(optionPanel, alterEgos.Enabled ? "Apply the included Alter Egos files to the game." : "Not included in this package.", 16, 42, 550, 22, 9, Muted, FontStyle.Regular);
        alterEgos.CheckedChanged += delegate { RefreshReadiness(); };
        var iconPanel = new InfoCard { Location = new Point(280, 440), Size = new Size(600, 116) }; Controls.Add(iconPanel);
        LabelAt(iconPanel, "IN-GAME BUTTON ICONS", 16, 8, 560, 20, 9, Muted, FontStyle.Bold);
        playStationIcons.Text = "PlayStation"; playStationIcons.SetBounds(16, 30, 278, 64); playStationIcons.Checked = true;
        xboxIcons.Text = "Xbox"; xboxIcons.SetBounds(306, 30, 278, 64); xboxIcons.Enabled = Program.Package.XboxIconsBytes > 0;
        foreach (var choice in new[] { playStationIcons, xboxIcons }) iconPanel.Controls.Add(choice);
        LabelAt(iconPanel, "Button icons can be changed later in UFC 6 Parsectagon Overlay.", 16, 96, 568, 17, 8, Muted, FontStyle.Regular);
        xboxIcons.CheckedChanged += delegate { RefreshReadiness(); };
        overlayPanel = new InfoCard { Location = new Point(280, 556), Size = new Size(600, 188), BackColor = Color.FromArgb(35, 38, 47), BorderColor = Color.FromArgb(78, 83, 98) }; Controls.Add(overlayPanel);
        overlayLogo.SetBounds(16, 14, 56, 56); overlayLogo.SizeMode = PictureBoxSizeMode.Zoom;
        using (var stream = Program.Assembly.GetManifestResourceStream("overlay-logo.png"))
        using (var image = Image.FromStream(stream)) overlayLogo.Image = new Bitmap(image);
        overlayPanel.Controls.Add(overlayLogo);
        LabelAt(overlayPanel, "OPTIONAL TOOL", 84, 12, 490, 20, 9, Accent, FontStyle.Bold);
        overlayTitle = LabelAt(overlayPanel, "UFC 6 Parsectagon Overlay", 82, 33, 500, 37, 17, Color.White, FontStyle.Bold);
        var overlayInfo = new InfoCard { Location = new Point(16, 76), Size = new Size(568, 44), BackColor = Color.FromArgb(27, 29, 36), BorderColor = Color.FromArgb(48, 52, 62) }; overlayPanel.Controls.Add(overlayInfo);
        overlayDescription = LabelAt(overlayInfo, "Controller-friendly cheats, screenshots, Photo Mode, settings,\nand restart/quit actions on compatible UFC 6 engines.", 10, 5, 548, 36, 9, Muted, FontStyle.Regular);
        overlayNote = LabelAt(overlayPanel, "Choose a PORTHEON or Kyty UFC 6 engine folder when prompted.", 16, 122, 568, 22, 9, Color.White, FontStyle.Regular);
        ButtonAt(overlayGithub, "Get from GitHub", 16, 146, 190, false); overlayGithub.Height = 32; overlayPanel.Controls.Add(overlayGithub);
        overlayGithub.Click += delegate { if (toolSlots[0] != null) Process.Start(new ProcessStartInfo(PortheonDownloader.ReleasesPageFor(toolSlots[0])) { UseShellExecute = true }); };
        ButtonAt(toolInstructions[0], "Instructions", 215, 146, 110, false); toolInstructions[0].Height = 32; overlayPanel.Controls.Add(toolInstructions[0]);
        toolInstructions[0].Click += delegate { ShowToolInstructions(0); };
        installOverlay.Text = "Install it for me"; installOverlay.SetBounds(334, 140, 245, 28);
        installOverlay.Font = new Font("Segoe UI", 10, FontStyle.Bold); installOverlay.Checked = false; overlayPanel.Controls.Add(installOverlay);
        LabelAt(overlayPanel, "Latest release · Internet required", 334, 167, 245, 17, 8, Muted, FontStyle.Regular);
        launcherPanel = new InfoCard { Location = new Point(280, 756), Size = new Size(600, 188), BackColor = Color.FromArgb(35, 38, 47), BorderColor = Color.FromArgb(78, 83, 98) }; Controls.Add(launcherPanel);
        portheonLogo.SetBounds(16, 14, 56, 56); portheonLogo.SizeMode = PictureBoxSizeMode.Zoom;
        using (var stream = Program.Assembly.GetManifestResourceStream("portheon-logo.png"))
        using (var image = Image.FromStream(stream)) portheonLogo.Image = new Bitmap(image);
        launcherPanel.Controls.Add(portheonLogo);
        LabelAt(launcherPanel, "OPTIONAL TOOL", 84, 12, 490, 20, 9, Accent, FontStyle.Bold);
        launcherTitle = LabelAt(launcherPanel, "Play on PC with PORTHEON", 82, 33, 500, 37, 17, Color.White, FontStyle.Bold);
        var launcherInfo = new InfoCard { Location = new Point(16, 76), Size = new Size(568, 44), BackColor = Color.FromArgb(27, 29, 36), BorderColor = Color.FromArgb(48, 52, 62) }; launcherPanel.Controls.Add(launcherInfo);
        launcherDescription = LabelAt(launcherInfo, "A free launcher for UFC 1–6 and other PC ports. It finds your games\nand chooses settings for your PC.", 10, 5, 548, 36, 9, Muted, FontStyle.Regular);
        launcherNote = LabelAt(launcherPanel, "Already have it? Use Play now on the final page after installation.", 16, 122, 565, 22, 9, Color.White, FontStyle.Regular);
        ButtonAt(playNow, "Play now", 16, 146, 108, true); playNow.Height = 32;
        launcherPanel.Controls.Add(playNow); playNow.Enabled = false;
        playNow.Click += delegate { OpenPortheon(); };
        ButtonAt(portheonGithub, "Get from GitHub", 16, 146, 190, false); portheonGithub.Height = 32;
        portheonGithub.Click += delegate { if (toolSlots[1] != null) Process.Start(new ProcessStartInfo(PortheonDownloader.ReleasesPageFor(toolSlots[1])) { UseShellExecute = true }); }; launcherPanel.Controls.Add(portheonGithub);
        ButtonAt(toolInstructions[1], "Instructions", 215, 146, 110, false); toolInstructions[1].Height = 32; launcherPanel.Controls.Add(toolInstructions[1]);
        toolInstructions[1].Click += delegate { ShowToolInstructions(1); };
        installPortheon.Text = "Install it for me"; installPortheon.SetBounds(334, 140, 245, 28);
        installPortheon.Font = new Font("Segoe UI", 10, FontStyle.Bold); installPortheon.Cursor = Cursors.Hand;
        installPortheon.Checked = false; launcherPanel.Controls.Add(installPortheon);
        LabelAt(launcherPanel, "Latest release · Internet required", 334, 167, 245, 17, 8, Muted, FontStyle.Regular);
        var progressCard = new InfoCard { Location = new Point(280, 956), Size = new Size(600, 156) }; Controls.Add(progressCard);
        LabelAt(progressCard, "Installation mode", 16, 11, 148, 24, 10, Color.White, FontStyle.Bold);
        performanceMode.SetBounds(170, 8, 180, 28);
        performanceMode.AccessibleName = "Installation performance mode";
        performanceMode.SelectedIndex = (int)InstallationMode.Balanced; progressCard.Controls.Add(performanceMode);
        modeHint.SetBounds(366, 13, 218, 22); modeHint.ForeColor = Muted; modeHint.Font = new Font("Segoe UI", 8.5f); progressCard.Controls.Add(modeHint);
        modeHint.Text = InstallationPerformance.Hint;
        performanceMode.SelectedIndexChanged += delegate
        {
            InstallationPerformance.Mode = (InstallationMode)performanceMode.SelectedIndex;
            modeHint.Text = InstallationPerformance.Hint;
        };
        status.SetBounds(16, 49, 490, 27); status.Font = new Font("Segoe UI", 11, FontStyle.Bold); progressCard.Controls.Add(status);
        percent.SetBounds(510, 49, 74, 27); percent.TextAlign = ContentAlignment.TopRight; percent.ForeColor = Muted; progressCard.Controls.Add(percent);
        progress.SetBounds(16, 88, 568, 7); progressCard.Controls.Add(progress);
        timing.SetBounds(16, 109, 568, 30); timing.ForeColor = Muted; timing.Font = new Font("Segoe UI", 9); progressCard.Controls.Add(timing);
        var footer = new Panel { BackColor = Color.FromArgb(49, 51, 60), Location = new Point(280, 620), Size = new Size(600, 1) }; Controls.Add(footer);
        var about = new LinkLabel { Text = "About / licenses", AutoSize = true, Location = new Point(280, 655),
            LinkColor = Muted, ActiveLinkColor = Color.White, VisitedLinkColor = Muted, LinkBehavior = LinkBehavior.HoverUnderline, TabStop = true };
        about.LinkClicked += delegate { ShowAbout(); }; Controls.Add(about);
        var viewLog = new LinkLabel { Text = "View log", AutoSize = true, Location = new Point(430, 655),
            LinkColor = Muted, ActiveLinkColor = Color.White, VisitedLinkColor = Muted, LinkBehavior = LinkBehavior.HoverUnderline };
        viewLog.LinkClicked += delegate
        {
            string log = InstallLog.FilePath;
            if (log != null) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + log + "\"") { UseShellExecute = true });
        }; Controls.Add(viewLog);
        ButtonAt(cancel, "Close", 550, 637, 90, false);
        ButtonAt(back, "Back", 650, 637, 90, false);
        ButtonAt(install, "Next", 750, 637, 130, true);
        back.Click += delegate { ShowPage(currentPage - 1); };
        install.Click += async delegate { if (complete) { if (launcherPending || overlayPending) await StartOptionalSetups(); else Close(); } else if (currentPage < 2) ShowPage(currentPage + 1); else await StartInstall(); };
        cancel.Click += delegate
        {
            if (running) CancelInstallation();
            else if (complete) Process.Start(new ProcessStartInfo(installedDestination) { UseShellExecute = true });
            else Close();
        };
        folder.TextChanged += delegate { RefreshReadiness(); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; CancelInstallation(); } };
        timer.Interval = 1000;
        timer.Tick += delegate { modeHint.Text = InstallationPerformance.Hint; if (running) UpdateTiming(); else if (!complete) RefreshReadiness(); };
        // Reuse the existing cards on three compact pages; choices survive navigation.
        foreach (var page in pages)
        { page.SetBounds(280, 180, 600, 420); page.BackColor = Background; Controls.Add(page); }
        foreach (Control card in new Control[] { installedSize.Parent, fileCount.Parent, destinationCard, optionPanel, iconPanel })
        { Point position = card.Location; pages[0].Controls.Add(card); card.Location = new Point(position.X - 280, position.Y - 136); }
        // The package size card is the remaining stat at the original position.
        Control packageCard = null;
        foreach (Control control in Controls)
            if (control is InfoCard && control.Left == 280 && control.Top == 136) { packageCard = control; break; }
        pages[0].Controls.Add(packageCard); packageCard.Location = Point.Empty;
        pages[1].Controls.Add(overlayPanel); overlayPanel.Location = Point.Empty;
        pages[1].Controls.Add(launcherPanel); launcherPanel.Location = new Point(0, 200);
        optionalToolsStatus.SetBounds(0, 394, 600, 22); optionalToolsStatus.Font = new Font("Segoe UI", 9);
        optionalToolsStatus.TextAlign = ContentAlignment.MiddleCenter; optionalToolsStatus.ForeColor = Muted;
        pages[1].Controls.Add(optionalToolsStatus);
        ApplyToolSlots(null);
        pages[2].Controls.Add(progressCard); progressCard.Location = new Point(0, 206);
        var summaryCard = new InfoCard { Size = new Size(600, 190) }; pages[2].Controls.Add(summaryCard);
        LabelAt(summaryCard, "YOUR INSTALLATION", 16, 12, 240, 24, 10, Color.White, FontStyle.Bold);
        summaryTotals = LabelAt(summaryCard, "", 256, 15, 328, 20, 9, Muted, FontStyle.Regular);
        summaryTotals.TextAlign = ContentAlignment.TopRight;
        var summaryPath = new InfoCard { Location = new Point(16, 42), Size = new Size(568, 44), BackColor = Color.FromArgb(27, 29, 36), BorderColor = Color.FromArgb(48, 52, 62) };
        summaryCard.Controls.Add(summaryPath);
        LabelAt(summaryPath, "DESTINATION", 10, 4, 548, 15, 8, Muted, FontStyle.Regular);
        summaryDestination = LabelAt(summaryPath, "", 10, 19, 548, 22, 10, Color.White, FontStyle.Regular);
        summaryDestination.AutoEllipsis = true;
        LabelAt(summaryCard, "ALTER EGOS", 16, 98, 270, 18, 8, Muted, FontStyle.Regular);
        summaryAlterEgos = LabelAt(summaryCard, "", 16, 117, 270, 25, 11, Color.White, FontStyle.Bold);
        LabelAt(summaryCard, "BUTTON ICONS", 306, 98, 278, 18, 8, Muted, FontStyle.Regular);
        summaryIcons = LabelAt(summaryCard, "", 306, 117, 278, 25, 11, Color.White, FontStyle.Bold);
        summaryCard.Controls.Add(new Panel { Location = new Point(16, 150), Size = new Size(568, 1), BackColor = Color.FromArgb(55, 59, 70) });
        LabelAt(summaryCard, "OPTIONAL TOOLS", 16, 162, 124, 20, 8, Muted, FontStyle.Regular);
        summaryTools = LabelAt(summaryCard, "", 144, 158, 440, 26, 10, Color.White, FontStyle.Bold);
        pages[2].Controls.Add(playNow); playNow.SetBounds(0, 378, 180, 36);
        LabelAt(pages[2], "Available after installation · Opens PORTHEON", 194, 386, 406, 24, 9, Muted, FontStyle.Regular);
        steps.SetBounds(280, 136, 600, 38); Controls.Add(steps);
        ShowPage(0);
        timer.Start(); RefreshReadiness();
        Shown += delegate
        {
            using (var graphics = CreateGraphics()) ConfigureViewport(Screen.FromControl(this).WorkingArea.Size, graphics.DpiX / 96f);
            ActiveControl = currentPage == 0 ? (Control)folder : install; folder.SelectionStart = folder.TextLength; folder.SelectionLength = 0;
            AutoScrollPosition = Point.Empty;
        };
        AcceptButton = install;
        // Every control is now present. Scale its design bounds once; point-size
        // fonts already render at the system DPI. Early WinForms autoscaling can
        // otherwise resize the form before these dynamically added controls exist.
        using (var graphics = CreateGraphics())
        { float scale = graphics.DpiX / 96f; if (Math.Abs(scale - 1f) > 0.001f) Scale(new SizeF(scale, scale)); }
    }

    internal void ShowPage(int index)
    {
        if (running || complete) return;
        currentPage = Math.Max(0, Math.Min(2, index));
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == currentPage;
        }
        steps.CurrentStep = currentPage; steps.AccessibleName = "Step " + (currentPage + 1) + " of 3: " + new[] { "Game setup", "Optional tools", "Install" }[currentPage]; steps.Invalidate();
        pageTitle.Text = new[] { "Make room for the fight.", "Make it your game.", "Ready for the fight." }[currentPage];
        install.Text = currentPage == 2 ? (existingGameDetected ? "Set up optional tools" : "Install game") : "Next";
        back.Enabled = currentPage > 0;
        AutoScrollPosition = Point.Empty;
        RefreshReadiness();
        if (Visible) ActiveControl = currentPage == 0 ? (Control)folder : install;
        if (currentPage == 1 && !optionalToolsChecked) CheckOptionalToolsAvailability();
    }

    private async void CheckOptionalToolsAvailability()
    {
        optionalToolsChecked = true; optionalToolsStatus.Text = "Checking access to optional tools…";
        overlayGithub.Enabled = portheonGithub.Enabled = installOverlay.Enabled = installPortheon.Enabled = false;
        optionalToolsCheck = new CancellationTokenSource();
        try
        {
            bool online = await Task.Run(delegate { return PortheonDownloader.CanReachGitHub(optionalToolsCheck.Token); });
            PortheonDownloader.OptionalToolsConfig config = null;
            string message = null;
            if (online)
            {
                try { config = await Task.Run(delegate { return PortheonDownloader.GetOptionalToolsConfig(optionalToolsCheck.Token); }); }
                catch (Exception ex) { InstallLog.Write("Optional tools configuration unavailable: " + ex.Message); message = "Optional tool list unavailable · Game installation remains available"; }
            }
            if (!IsDisposed && !optionalToolsCheck.IsCancellationRequested)
            {
                ApplyToolSlots(config);
                SetOptionalToolsOnline(online, message);
            }
        }
        catch (OperationCanceledException) { }
        finally { if (optionalToolsCheck != null) { optionalToolsCheck.Dispose(); optionalToolsCheck = null; } }
    }

    internal void SetOptionalToolsOnline(bool online, string message)
    {
        optionalToolsOnline = online;
        overlayGithub.Enabled = toolInstructions[0].Enabled = toolSlots[0] != null && online && !running;
        portheonGithub.Enabled = toolInstructions[1].Enabled = toolSlots[1] != null && online && !running;
        installOverlay.Enabled = toolSlots[0] != null && toolSlots[0].installMode == "installer" && online && !running;
        installPortheon.Enabled = toolSlots[1] != null && toolSlots[1].installMode == "installer" && online && !running;
        if (online)
        {
            optionalToolsStatus.ForeColor = Green;
            optionalToolsStatus.Text = message ?? "Optional tools loaded from GitHub";
        }
        else
        {
            optionalToolsStatus.ForeColor = Accent;
            optionalToolsStatus.Text = message ?? "No internet connection to GitHub · Optional tools are unavailable";
        }
        if (summaryDestination != null) UpdateSummary();
    }

    internal void ApplyToolSlots(PortheonDownloader.OptionalToolsConfig config)
    {
        for (int i = 0; i < 2; i++) toolSlots[i] = config != null && config.slots[i].enabled ? config.slots[i] : null;
        if (toolSlots[0] == null || toolSlots[0].installMode != "installer") { installOverlay.Checked = false; overlayPending = false; }
        if (toolSlots[1] == null || toolSlots[1].installMode != "installer") { installPortheon.Checked = false; launcherPending = false; }
        overlayPanel.Visible = toolSlots[0] != null;
        launcherPanel.Visible = toolSlots[1] != null;
        launcherPanel.Top = toolSlots[0] == null ? 0 : 200;
        optionalToolsStatus.Top = toolSlots[0] != null && toolSlots[1] != null ? 394 : (toolSlots[0] != null || toolSlots[1] != null ? 194 : 12);
        if (toolSlots[0] != null) { overlayTitle.Text = toolSlots[0].name; overlayDescription.Text = toolSlots[0].description; overlayNote.Text = toolSlots[0].note; overlayLogo.Visible = toolSlots[0].repository == "thyaguster/ufc6-actions-overlay"; }
        if (toolSlots[1] != null) { launcherTitle.Text = toolSlots[1].name; launcherDescription.Text = toolSlots[1].description; launcherNote.Text = toolSlots[1].note; portheonLogo.Visible = toolSlots[1].repository == "XAndromedaXR/PORTHEON"; }
        installOverlay.Visible = toolSlots[0] != null && toolSlots[0].installMode == "installer";
        installPortheon.Visible = toolSlots[1] != null && toolSlots[1].installMode == "installer";
        overlayTitle.Left = overlayLogo.Visible ? 82 : 16;
        launcherTitle.Left = portheonLogo.Visible ? 82 : 16;
        SetOptionalToolsOnline(optionalToolsOnline, null);
        if (summaryDestination != null) UpdateSummary();
    }

    private void ShowToolInstructions(int index)
    {
        var slot = toolSlots[index];
        if (slot == null) return;
        MessageBox.Show(this, slot.instructions + "\n\nLatest release: " + PortheonDownloader.ReleasesPageFor(slot),
            slot.name + " · Installation instructions", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void UpdateSummary()
    {
        summaryDestination.Text = folder.Text;
        summaryAlterEgos.Text = existingGameDetected ? "Unchanged" : alterEgos.Checked ? "Included" : "Off";
        summaryIcons.Text = existingGameDetected ? "Unchanged" : xboxIcons.Checked ? "Xbox" : "PlayStation";
        summaryTools.Text = (installOverlay.Checked && toolSlots[0] != null ? toolSlots[0].name : "") +
            (installOverlay.Checked && installPortheon.Checked ? " + " : "") + (installPortheon.Checked && toolSlots[1] != null ? toolSlots[1].name : "") +
            (!installOverlay.Checked && !installPortheon.Checked ? "None selected" : "");
        summaryTotals.Text = existingGameDetected ? "Existing game · no game files changed" : installedSize.Text + " installed  ·  " + fileCount.Text + " files";
    }

    internal void ScalePreview(float ratio)
    {
        var fonts = new System.Collections.Generic.Dictionary<Control, Font>();
        CaptureFonts(this, fonts);
        AutoScaleMode = AutoScaleMode.None; Scale(new SizeF(ratio, ratio));
        foreach (var item in fonts) item.Key.Font = new Font(item.Value.FontFamily, item.Value.Size * ratio, item.Value.Style);
    }
    private static void CaptureFonts(Control control, System.Collections.Generic.Dictionary<Control, Font> fonts)
    { fonts[control] = control.Font; foreach (Control child in control.Controls) CaptureFonts(child, fonts); }

    internal void ConfigureViewport(Size screen, float scale)
    {
        int fullWidth = (int)(920 * scale);
        if (!condensed && fullWidth > screen.Width - 48)
        {
            hero.Visible = false; condensed = true;
            foreach (Control control in Controls) if (control != hero) control.Left -= (int)(240 * scale);
        }
        int width = (int)((condensed ? 680 : 920) * scale);
        ClientSize = new Size(Math.Min(width, screen.Width - 48), Math.Min((int)(700 * scale), screen.Height - 64));
        AutoScrollMinSize = new Size((int)((condensed ? 660 : 900) * scale), (int)(700 * scale));
    }

    private void RefreshReadiness()
    {
        if (running || complete) return;
        existingGameDetected = false;
        bool valid = false;
        try
        {
            if (String.IsNullOrWhiteSpace(folder.Text)) throw new IOException("Choose a destination folder.");
            string path = Path.GetFullPath(folder.Text);
            if (String.IsNullOrEmpty(Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)))) throw new IOException("Choose a game folder, rather than a drive root.");
            if (Directory.Exists(path))
            {
                if (!IsUfc6Folder(path)) throw new IOException("This folder already exists. Choose a new folder name.");
                existingGameDetected = true;
                space.Text = "Existing UFC 6 installation detected · Game files will not be changed";
                space.ForeColor = Green;
            }
            else
            {
                if (File.Exists(path)) throw new IOException("This path is an existing file. Choose a new folder name.");
                var drive = new DriveInfo(Path.GetPathRoot(path));
                long required = Program.RequiredSpace(alterEgos.Checked, xboxIcons.Checked);
                space.Text = Program.Size(drive.AvailableFreeSpace) + " free on " + drive.Name + "  ·  " + Program.Size(required) + " required";
                space.ForeColor = Green;
                if (drive.AvailableFreeSpace < required) throw new IOException("Not enough space on this drive. Choose another destination.");
            }
            valid = true;
        }
        catch (Exception ex)
        { space.Text = ex.Message; space.ForeColor = Accent; }
        alterEgos.Enabled = !existingGameDetected && Program.Package.AlterEgosArchiveBytes > 0;
        playStationIcons.Enabled = !existingGameDetected;
        xboxIcons.Enabled = !existingGameDetected && Program.Package.XboxIconsBytes > 0;
        installedSize.Text = existingGameDetected ? "Unchanged" : (xboxIcons.Checked ? "~" : "") + Program.Size(Program.Package.UnpackedBytes + (alterEgos.Checked ? Program.Package.AlterEgosInstalledDeltaBytes : 0) + (xboxIcons.Checked ? Program.XboxOverhead : 0));
        fileCount.Text = existingGameDetected ? "Unchanged" : (Program.Package.FileCount + (alterEgos.Checked ? Program.Package.AlterEgosFileDelta : 0) + (xboxIcons.Checked ? 2 : 0)).ToString("N0");
        bool packageReady = false;
        try { packageReady = Program.PackageError == null && Program.PackageSource != null && Program.PackageSource.Length == Program.Package.ArchiveBytes; } catch (IOException) { }
        bool hasOptionalTool = installOverlay.Checked || installPortheon.Checked;
        install.Enabled = currentPage < 2 ? valid || !packageReady : valid && packageReady && (!existingGameDetected || hasOptionalTool);
        if (currentPage == 2) install.Text = existingGameDetected ? "Set up optional tools" : "Install game";
        UpdateSummary();
        if (!packageReady)
        {
            description.Text = Program.PackageError ?? "data.bin changed. Restart setup to load the current package.";
            if (!attempted) { status.Text = "Package unavailable"; status.ForeColor = Accent; }
        }
        else
        {
            description.Text = currentPage == 0 ? (existingGameDetected ? "Existing game detected. Only optional tools can be set up." : "Choose a destination and your game options.") : currentPage == 1 ? "Optional downloads from the official GitHub releases." : existingGameDetected ? "Existing game detected. Select at least one optional tool to continue." : "Review your choices, then install your game files.";
            if (currentPage == 2 && !valid) description.Text = space.Text;
            if (!attempted) { status.Text = valid ? (existingGameDetected ? "Existing game ready" : "Ready to install") : "Check your destination"; status.ForeColor = valid ? Green : Accent; }
        }
        if (!attempted) { timing.Text = "Package integrity will be checked before installation."; percent.Text = "0%"; }
    }

    private static bool IsUfc6Folder(string path)
    {
        return File.Exists(Path.Combine(path, "sce_sys", "param.json")) && File.Exists(Path.Combine(path, "eboot.bin")) ||
            File.Exists(Path.Combine(path, "UFC 6.exe")) || File.Exists(Path.Combine(path, "bin", "UFC 6.exe"));
    }

    private async Task StartInstall()
    {
        RefreshReadiness(); if (!install.Enabled) return;
        if (existingGameDetected)
        {
            InstallLog.PortheonRequested = installPortheon.Checked;
            InstallLog.OverlayRequested = installOverlay.Checked;
            attempted = complete = true;
            installedDestination = folder.Text;
            launcherPending = installPortheon.Checked; overlayPending = installOverlay.Checked;
            installOverlay.Enabled = installPortheon.Enabled = false;
            browse.Enabled = folder.Enabled = alterEgos.Enabled = false;
            playStationIcons.Enabled = xboxIcons.Enabled = false;
            back.Enabled = false;
            status.Text = "Existing game ready"; status.ForeColor = Green;
            description.Text = "Game files will not be changed. Preparing selected optional tools.";
            timing.Text = "Existing UFC 6 installation detected.";
            InstallLog.Write("Existing UFC 6 installation detected; game extraction skipped.");
            await StartOptionalSetups();
            return;
        }
        bool withAlterEgos = alterEgos.Checked;
        bool withXboxIcons = xboxIcons.Checked;
        InstallLog.PortheonRequested = installPortheon.Checked;
        InstallLog.OverlayRequested = installOverlay.Checked;
        installOverlay.Enabled = false;
        installPortheon.Enabled = false;
        playStationIcons.Enabled = xboxIcons.Enabled = false;
        back.Enabled = false;
        attempted = true; running = true; install.Enabled = browse.Enabled = folder.Enabled = alterEgos.Enabled = false;
        cancel.Text = "Cancel"; cancel.Enabled = true; cancellation = new CancellationTokenSource();
        elapsed.Restart(); phase = -1; phasePercent = 0; progress.Value = 0; progress.Finished = false;
        installedDestination = folder.Text; status.ForeColor = Color.White;
        var updates = new Progress<Tuple<int, string>>(delegate(Tuple<int, string> value)
        {
            if (!running || phase == 4 || cancellation.IsCancellationRequested) return;
            int nextPhase = value.Item2 == "Installing Xbox button icons..." ? 3 : value.Item2 == "Installing Alter Egos..." ? 2 : value.Item1 < 10 ? 0 : 1;
            if (phase != nextPhase) { phase = nextPhase; phaseElapsed.Restart(); }
            phasePercent = phase == 0 ? value.Item1 * 10 : phase == 2 ? Math.Min(100, (value.Item1 - 90) * 100.0 / 9) : Math.Min(100, (value.Item1 - 10) * 100.0 / (withAlterEgos ? 79 : 89));
            progress.Value = value.Item1; progress.Invalidate(); percent.Text = value.Item1 + "%";
            status.Text = phase == 0 ? "01  /  Checking package" : phase == 3 ? (withAlterEgos ? "04" : "03") + "  /  Installing Xbox button icons" : phase == 2 ? "03  /  Installing Alter Egos" : "02  /  Installing game files"; UpdateTiming();
        });
        try
        {
            await Task.Run(delegate { Program.Install(installedDestination, cancellation.Token, delegate(int p, string s) { ((IProgress<Tuple<int, string>>)updates).Report(Tuple.Create(p, s)); }, withAlterEgos, withXboxIcons); });
            complete = true; status.Text = "Installation complete"; status.ForeColor = Green;
            progress.Value = 100; progress.Finished = true; progress.Invalidate(); percent.Text = "100%";
            description.Text = "Your game files are ready in the selected folder.";
            timing.Text = "Completed in " + Duration(elapsed.Elapsed) + "."; install.Text = "Finish"; cancel.Text = "Open folder";
        }
        catch (OperationCanceledException) { InstallLog.Write("CANCELLED: partial installation cleaned up."); status.Text = "Installation cancelled"; status.ForeColor = Muted; timing.Text = "Temporary files removed. You can start again."; progress.Value = 0; progress.Invalidate(); percent.Text = "0%"; }
        catch (Exception ex) { InstallLog.Failure(ex); status.Text = "Installation could not complete"; status.ForeColor = Accent; timing.Text = "Resolve the problem, then try again. See View log for details."; MessageBox.Show(this, ex.Message + "\n\nDiagnostic log: " + InstallLog.FilePath, "Setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            running = false; elapsed.Stop(); phaseElapsed.Stop(); cancellation.Dispose(); cancellation = null;
            browse.Enabled = folder.Enabled = !complete; cancel.Enabled = true;
            alterEgos.Enabled = !complete && Program.Package.AlterEgosArchiveBytes > 0;
            playStationIcons.Enabled = !complete; xboxIcons.Enabled = !complete && Program.Package.XboxIconsBytes > 0;
            playNow.Enabled = complete;
            performanceMode.Enabled = !complete;
            if (!complete) { back.Enabled = true; cancel.Text = "Close"; RefreshReadiness(); } else install.Enabled = true;
            installPortheon.Enabled = optionalToolsOnline && toolSlots[1] != null && toolSlots[1].installMode == "installer";
            installOverlay.Enabled = optionalToolsOnline && toolSlots[0] != null && toolSlots[0].installMode == "installer";
        }
        if (complete)
        {
            launcherPending = installPortheon.Checked; overlayPending = installOverlay.Checked;
            if (launcherPending || overlayPending) await StartOptionalSetups();
        }
    }

    private async Task StartOptionalSetups()
    {
        running = true; install.Enabled = playNow.Enabled = false; cancel.Text = "Cancel"; cancel.Enabled = true;
        cancellation = new CancellationTokenSource(); phase = 4; elapsed.Restart();
        progress.Value = 0; progress.Finished = false; progress.Invalidate(); percent.Text = "0%";
        description.Text = existingGameDetected ? "Existing game is unchanged. Preparing your optional setup choices." : "Your game is installed. Preparing your optional setup choices.";
        status.ForeColor = Color.White;
        string product = launcherPending ? toolSlots[1].name : toolSlots[0].name;
        var updates = new Progress<Tuple<int, string>>(delegate(Tuple<int, string> value)
        {
            if (!running || phase != 4 || cancellation.IsCancellationRequested) return;
            progress.Value = value.Item1; progress.Invalidate(); percent.Text = value.Item1 + "%";
            status.Text = value.Item2; UpdateTiming();
        });
        Action<int, string> report = delegate(int p, string text) { ((IProgress<Tuple<int, string>>)updates).Report(Tuple.Create(p, text)); };
        try
        {
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UFC6 Installer", "Downloads");
            var pending = new System.Collections.Generic.List<int>();
            if (overlayPending) pending.Add(0);
            if (launcherPending) pending.Add(1);
            pending.Sort(delegate(int a, int b) { int order = toolSlots[a].installOrder.CompareTo(toolSlots[b].installOrder); return order != 0 ? order : a.CompareTo(b); });
            for (int position = 0; position < pending.Count; position++)
            {
                int index = pending[position];
                var slot = toolSlots[index];
                product = slot.name;
                string selectedFolder = null;
                if (slot.chooseFolder)
                {
                    using (var picker = new FolderBrowserDialog { Description = slot.folderPrompt, ShowNewFolderButton = false })
                    {
                        if (picker.ShowDialog(this) != DialogResult.OK) throw new OperationCanceledException("Optional tool folder selection cancelled.");
                        selectedFolder = Path.GetFullPath(picker.SelectedPath);
                    }
                    if (!Directory.Exists(selectedFolder)) throw new IOException("The selected folder no longer exists.");
                }
                string found = DetectOptionalTool(slot);
                string gameFolder = FindOverlayGameFolder(selectedFolder ?? installedDestination);
                if (found == null && slot.repository == "XAndromedaXR/PORTHEON") found = FindPortheon();
                else if (found == null && slot.repository == "thyaguster/ufc6-actions-overlay" && IsOverlayInstalled(gameFolder)) found = gameFolder;
                if (!String.IsNullOrEmpty(found) && !ConfirmRerunSetup(product, found))
                {
                    InstallLog.Write("Existing optional tool kept: " + slot.name + " at " + found);
                    status.Text = "Existing " + slot.name + " kept";
                }
                else
                {
                    InstallLog.Write("Optional setup requested: " + slot.name);
                    string executable = await Task.Run(delegate { return PortheonDownloader.DownloadLatest(cache, cancellation.Token, report, slot); });
                    cancellation.Token.ThrowIfCancellationRequested();
                    var info = new ProcessStartInfo(executable) { UseShellExecute = true };
                    if (!String.IsNullOrWhiteSpace(slot.installerArguments))
                    {
                        string arguments = slot.installerArguments.Replace("{gameFolder}", QuoteWindowsPath(installedDestination));
                        if (slot.chooseFolder) arguments = arguments.Replace("{selectedFolder}", QuoteWindowsPath(selectedFolder));
                        info.Arguments = arguments;
                    }
                    else if (slot.repository == "thyaguster/ufc6-actions-overlay" && gameFolder != null && Directory.Exists(gameFolder)) info.Arguments = "--folder \"" + gameFolder + "\"";
                    using (Process setup = Process.Start(info))
                    {
                        if (setup == null) throw new IOException("Could not open the setup for " + slot.name + ".");
                        InstallLog.Write("Verified optional installer opened: " + executable);
                        status.Text = slot.name + " setup opened";
                        description.Text = slot.note;
                        if (position + 1 < pending.Count)
                        {
                            status.Text = "Complete " + slot.name + " setup to continue";
                            await Task.Run(delegate { while (!setup.WaitForExit(200)) cancellation.Token.ThrowIfCancellationRequested(); });
                            if (setup.ExitCode != 0) throw new IOException(slot.name + " setup exited with code " + setup.ExitCode + ". Complete it before continuing.");
                        }
                    }
                }
                if (index == 0) overlayPending = false; else launcherPending = false;
            }
            status.ForeColor = Green;
            timing.Text = existingGameDetected ? "Existing game kept. Complete the optional setup in its own window." : "Game files installed. Complete the optional setup in its own window.";
            progress.Value = 100; progress.Finished = true; progress.Invalidate(); percent.Text = "100%";
        }
        catch (OperationCanceledException)
        { InstallLog.Write("Optional setup cancelled; completed game and any open external setup retained."); status.Text = (existingGameDetected ? "Existing game" : "Game installed") + " · Optional setup cancelled"; status.ForeColor = Muted; timing.Text = "Game files kept. Finish any open setup window separately."; }
        catch (Exception ex)
        {
            InstallLog.Failure(ex); status.Text = (existingGameDetected ? "Existing game" : "Game installed") + " · Optional setup needs attention"; status.ForeColor = Accent;
            timing.Text = "Retry setup, or use the project's Get from GitHub button above.";
            MessageBox.Show(this, "Your game files are installed. " + product + " setup could not complete:\n\n" + ex.Message,
                "Optional setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            running = false; elapsed.Stop(); phase = -1; cancellation.Dispose(); cancellation = null;
            install.Text = launcherPending || overlayPending ? "Retry setup" : "Finish"; install.Enabled = true;
            cancel.Text = "Open folder"; cancel.Enabled = playNow.Enabled = true;
        }
    }

    private bool ConfirmRerunSetup(string product, string location)
    {
        return MessageBox.Show(this, product + " is already installed or available at:\n\n" + location +
            "\n\nRun its latest setup again?", product + " already detected", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private string DetectOptionalTool(PortheonDownloader.ToolSlot slot)
    {
        if (String.IsNullOrEmpty(slot.detectedFile)) return null;
        string candidate = slot.detectedFile;
        var roots = new[] { new { Token = "{gameFolder}", Value = installedDestination },
            new { Token = "{localAppData}", Value = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
            new { Token = "{programFiles}", Value = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
            new { Token = "{programFilesX86}", Value = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) } };
        foreach (var root in roots) if (candidate.StartsWith(root.Token, StringComparison.Ordinal))
        {
            candidate = root.Value + candidate.Substring(root.Token.Length);
            return File.Exists(candidate) ? candidate : null;
        }
        return null;
    }

    private static string QuoteWindowsPath(string path)
    {
        if (String.IsNullOrEmpty(path) || path.IndexOf('"') >= 0) throw new IOException("Invalid optional setup folder.");
        return "\"" + path + (path.EndsWith("\\", StringComparison.Ordinal) ? "\\" : "") + "\"";
    }

    private static bool IsOverlayInstalled(string folder)
    {
        string bin = FindOverlayGameFolder(folder);
        if (String.IsNullOrEmpty(bin)) return false;
        return File.Exists(Path.Combine(bin, "version.dll")) &&
            (File.Exists(Path.Combine(bin, "ufc6_menu_layer.dll")) || File.Exists(Path.Combine(bin, "ufc6_menu_layer.json")));
    }

    private static string FindOverlayGameFolder(string folder)
    {
        if (String.IsNullOrEmpty(folder)) return null;
        if (File.Exists(Path.Combine(folder, "UFC 6.exe"))) return folder;
        string bin = Path.Combine(folder, "bin");
        return File.Exists(Path.Combine(bin, "UFC 6.exe")) ? bin : null;
    }

    private string FindPortheon()
    {
        if (String.IsNullOrEmpty(portheonExecutable) || !File.Exists(portheonExecutable))
        {
            foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                foreach (string relative in new[] { Path.Combine("Programs", "PORTHEON", "PORTHEON.exe"), Path.Combine("PORTHEON", "PORTHEON.exe") })
                { string candidate = Path.Combine(root, relative); if (File.Exists(candidate)) portheonExecutable = candidate; }
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                try
                {
                    using (var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\PORTHEON.exe"))
                    {
                        string path = key == null ? null : key.GetValue(null) as string;
                        if (!String.IsNullOrEmpty(path) && File.Exists(path)) portheonExecutable = path;
                    }
                    using (var apps = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (apps == null) continue;
                        foreach (string name in apps.GetSubKeyNames())
                        using (var key = apps.OpenSubKey(name))
                        {
                            if (key == null) continue;
                            string title = key.GetValue("DisplayName") as string;
                            if (String.IsNullOrEmpty(title) || !title.StartsWith("PORTHEON", StringComparison.OrdinalIgnoreCase)) continue;
                            string location = key.GetValue("InstallLocation") as string;
                            if (!String.IsNullOrEmpty(location))
                            { string candidate = Path.Combine(location, "PORTHEON.exe"); if (File.Exists(candidate)) portheonExecutable = candidate; }
                        }
                    }
                }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        return String.IsNullOrEmpty(portheonExecutable) || !File.Exists(portheonExecutable) ? null : portheonExecutable;
    }

    private void OpenPortheon()
    {
        if (!complete || running) return;
        portheonExecutable = FindPortheon();
        if (String.IsNullOrEmpty(portheonExecutable) || !File.Exists(portheonExecutable))
        {
            using (var dialog = new OpenFileDialog { Title = "Locate your installed PORTHEON.exe", Filter = "PORTHEON|PORTHEON.exe", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!String.Equals(Path.GetFileName(dialog.FileName), "PORTHEON.exe", StringComparison.OrdinalIgnoreCase))
                { MessageBox.Show(this, "Choose PORTHEON.exe from your PORTHEON installation.", "Play on PC"); return; }
                portheonExecutable = dialog.FileName;
            }
        }
        try
        {
            Process.Start(new ProcessStartInfo(portheonExecutable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(portheonExecutable) });
            InstallLog.Write("PORTHEON opened: " + portheonExecutable);
            description.Text = "In PORTHEON, choose UFC 6. If needed, select this folder under Game files.";
        }
        catch (Exception ex) { InstallLog.Failure(ex); MessageBox.Show(this, ex.Message, "Could not open PORTHEON", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private void CancelInstallation()
    {
        if (complete) { cancel.Enabled = false; status.Text = "Cancelling optional setup…"; timing.Text = "Your installed game files will be kept."; cancellation.Cancel(); return; }
        cancel.Enabled = false; status.Text = "Cancelling installation…";
        timing.Text = "Please wait while temporary files are removed."; cancellation.Cancel();
    }
    private static string Duration(TimeSpan time)
    { return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss"); }
    private void UpdateTiming()
    {
        if (cancellation != null && cancellation.IsCancellationRequested) return;
        string remaining = "Estimating time for this step…";
        if (phase == 4) { timing.Text = "Optional setup elapsed " + Duration(elapsed.Elapsed) + "   ·   Game installation complete"; return; }
        if (phase == 3) { timing.Text = "Elapsed " + Duration(elapsed.Elapsed) + "   ·   Applying and verifying button artwork…"; return; }
        if (phaseElapsed.Elapsed.TotalSeconds >= 5 && phasePercent > 0 && phasePercent < 100)
        {
            double seconds = phaseElapsed.Elapsed.TotalSeconds * (100 - phasePercent) / phasePercent;
            remaining = "This step: about " + Duration(TimeSpan.FromSeconds(Math.Min(seconds, 864000))) + " remaining";
        }
        timing.Text = "Elapsed " + Duration(elapsed.Elapsed) + "   ·   " + remaining;
    }
    private void ShowAbout()
    {
        using (var reader = new StreamReader(Program.Assembly.GetManifestResourceStream("7zip-license.txt")))
        using (var dialog = new Form { Text = "About & licenses", Size = new Size(680, 520), StartPosition = FormStartPosition.CenterParent, BackColor = Background })
        {
            dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                BackColor = Background, ForeColor = Color.White, Font = new Font("Segoe UI", 10), BorderStyle = BorderStyle.None,
                Text = Program.Package.Title + " — Folder installer\r\n\r\nExtraction uses 7-Zip. Source: https://www.7-zip.org/ (download the source for the embedded version).\r\n\r\n" + reader.ReadToEnd() });
            dialog.ShowDialog(this);
        }
    }
    protected override void Dispose(bool disposing)
        { if (disposing) { timer.Dispose(); if (optionalToolsCheck != null) { optionalToolsCheck.Cancel(); optionalToolsCheck.Dispose(); } if (portheonLogo.Image != null) portheonLogo.Image.Dispose(); if (overlayLogo.Image != null) overlayLogo.Image.Dispose(); } base.Dispose(disposing); }
}
