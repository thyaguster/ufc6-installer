using System;
using System.IO;
using System.Reflection;

internal static class InstallLog
{
    private static readonly object Sync = new object();
    private static string path;
    internal static bool PortheonRequested = false;
    internal static bool OverlayRequested = false;
    internal static string FilePath
    {
        get { lock (Sync) { if (path == null) Begin("Session"); return path; } }
    }
    internal static void Begin(string operation)
    {
        lock (Sync)
        {
            string requested = Environment.GetEnvironmentVariable("UFC6_INSTALLER_LOG_DIR");
            string folder = String.IsNullOrWhiteSpace(requested) ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UFC6 Installer", "Logs") : requested;
            path = null;
            foreach (string candidate in new[] { folder, Path.Combine(Path.GetTempPath(), "UFC6 Installer", "Logs") })
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    string file = Path.Combine(candidate, "setup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
                    File.WriteAllText(file, "EA SPORTS UFC 6 Installer " + Assembly.GetExecutingAssembly().GetName().Version + Environment.NewLine);
                    path = file; break;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Write("START " + operation + "; OS=" + Environment.OSVersion + "; 64-bit=" + Environment.Is64BitOperatingSystem);
        }
    }
    internal static void Write(string message)
    {
        lock (Sync)
        {
            if (path == null) return;
            try { File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    internal static void Failure(Exception failure) { Write("ERROR " + failure); }
}
