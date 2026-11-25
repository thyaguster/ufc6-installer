using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

internal static class PortheonDownloader
{
    internal const string ReleasesPage = "https://github.com/XAndromedaXR/PORTHEON/releases/latest";
    private const string ReleaseApi = "https://api.github.com/repos/XAndromedaXR/PORTHEON/releases/latest";
    private const string DownloadPrefix = "https://github.com/XAndromedaXR/PORTHEON/releases/download/";
    internal const string OverlayReleasesPage = "https://github.com/thyaguster/ufc6-actions-overlay/releases/latest";
    private const string OverlayApi = "https://api.github.com/repos/thyaguster/ufc6-actions-overlay/releases/latest";
    private const string OverlayDownloadPrefix = "https://github.com/thyaguster/ufc6-actions-overlay/releases/download/";
    private const string OptionalToolsConfigUrl = "https://raw.githubusercontent.com/thyaguster/ufc6-installer/main/optional-tools.json";

    internal sealed class OptionalToolsConfig
    {
        public ToolSlot[] slots { get; set; }
    }
    internal sealed class ToolSlot
    {
        public bool enabled { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public string note { get; set; }
        public string repository { get; set; }
        public string installerPattern { get; set; }
        public string installMode { get; set; }
        public string instructions { get; set; }
        public int installOrder { get; set; }
        public string installerArguments { get; set; }
        public string detectedFile { get; set; }
        public bool chooseFolder { get; set; }
        public string folderPrompt { get; set; }
    }

    internal sealed class Release
    {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public Asset[] assets { get; set; }
    }
    internal sealed class Asset
    {
        public string name { get; set; }
        public long size { get; set; }
        public string digest { get; set; }
        public string browser_download_url { get; set; }
    }

    private static HttpWebRequest Request(string url)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.UserAgent = "UFC6-Folder-Installer/1.0";
        request.Accept = "application/vnd.github+json";
        request.Timeout = 30000; request.ReadWriteTimeout = 30000;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        return request;
    }

    // This checks access to GitHub only. The optional-tools screen should still
    // be useful when a player is offline: the game package itself never needs it.
    internal static bool CanReachGitHub(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var request = Request("https://api.github.com/");
        request.Method = "HEAD"; request.Timeout = 6000; request.ReadWriteTimeout = 6000;
        using (cancellation.Register(request.Abort))
        {
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                    return response.StatusCode >= HttpStatusCode.OK && response.StatusCode < HttpStatusCode.BadRequest;
            }
            catch (WebException) { cancellation.ThrowIfCancellationRequested(); return false; }
        }
    }

    internal static OptionalToolsConfig ParseOptionalToolsConfig(string json)
    {
        if (String.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024) throw new IOException("Optional-tools configuration is invalid.");
        OptionalToolsConfig config;
        try { config = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 }.Deserialize<OptionalToolsConfig>(json); }
        catch (Exception ex) { throw new IOException("Optional-tools configuration is invalid.", ex); }
        if (config == null || config.slots == null || config.slots.Length != 2) throw new IOException("Optional-tools configuration requires exactly two slots.");
        foreach (ToolSlot slot in config.slots)
        {
            if (slot == null) throw new IOException("Optional-tools slot is invalid.");
            if (!slot.enabled) continue;
            if (String.IsNullOrEmpty(slot.installMode)) slot.installMode = "installer";
            if (String.IsNullOrWhiteSpace(slot.instructions)) slot.instructions = slot.note;
            if (String.IsNullOrWhiteSpace(slot.name) || slot.name.Length > 42 || String.IsNullOrWhiteSpace(slot.description) || slot.description.Length > 160 ||
                String.IsNullOrWhiteSpace(slot.note) || slot.note.Length > 110 || slot.repository == null ||
                !Regex.IsMatch(slot.repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z") ||
                (slot.installMode != "installer" && slot.installMode != "github") || slot.installOrder < 0 || slot.installOrder > 1000 ||
                String.IsNullOrWhiteSpace(slot.instructions) || slot.instructions.Length > 1200 ||
                (slot.chooseFolder && (slot.installMode != "installer" || String.IsNullOrWhiteSpace(slot.folderPrompt) || slot.folderPrompt.Length > 150 ||
                String.IsNullOrWhiteSpace(slot.installerArguments) || !slot.installerArguments.Contains("{selectedFolder}"))) ||
                (slot.installerArguments != null && (slot.installerArguments.Length > 250 || slot.installerArguments.Contains("\r") || slot.installerArguments.Contains("\n") ||
                Regex.IsMatch(slot.installerArguments.Replace("{gameFolder}", "").Replace("{selectedFolder}", ""), @"\{[^{}]*\}") ||
                (!slot.chooseFolder && slot.installerArguments.Contains("{selectedFolder}")))) ||
                (slot.detectedFile != null && (slot.detectedFile.Length > 250 || !Regex.IsMatch(slot.detectedFile, @"\A\{(gameFolder|localAppData|programFiles|programFilesX86)\}\\[^:*?\""<>|]+\z") ||
                Regex.IsMatch(slot.detectedFile, @"(^|\\)\.\.(\\|$)"))) ||
                (slot.installMode == "installer" && (slot.installerPattern == null || !Regex.IsMatch(slot.installerPattern, @"\A[A-Za-z0-9._*-]+\.exe\z", RegexOptions.IgnoreCase) ||
                slot.installerPattern.IndexOf('*') != slot.installerPattern.LastIndexOf('*'))))
                throw new IOException("Optional-tools slot has invalid metadata.");
        }
        return config;
    }

    internal static OptionalToolsConfig GetOptionalToolsConfig(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var request = Request(OptionalToolsConfigUrl); request.Timeout = 6000; request.ReadWriteTimeout = 6000;
        using (cancellation.Register(request.Abort))
        {
            try
            {
                using (var response = request.GetResponse())
                using (var input = response.GetResponseStream())
                using (var reader = new StreamReader(input))
                {
                    string json = reader.ReadToEnd(); cancellation.ThrowIfCancellationRequested();
                    return ParseOptionalToolsConfig(json);
                }
            }
            catch (WebException) { cancellation.ThrowIfCancellationRequested(); throw; }
        }
    }

    internal static Asset SelectInstaller(Release release, bool overlay = false)
    {
        string product = overlay ? "UFC 6 Parsectagon Overlay" : "PORTHEON";
        string pattern = overlay ? @"^UFC6-Actions-Overlay-Setup-[A-Za-z0-9._-]+\.exe$" : @"^PORTHEON-[A-Za-z0-9._-]+-Setup\.exe$";
        string prefix = overlay ? OverlayDownloadPrefix : DownloadPrefix;
        if (release == null || release.draft || release.prerelease || release.assets == null)
            throw new IOException("GitHub did not return a stable " + product + " release.");
        Asset selected = null;
        foreach (var asset in release.assets)
        {
            if (asset.name == null || !Regex.IsMatch(asset.name, pattern, RegexOptions.IgnoreCase)) continue;
            if (selected != null) throw new IOException("The latest " + product + " release contains multiple Windows installers.");
            if (asset.size <= 0 || asset.size > 4L * 1024 * 1024 * 1024 || asset.browser_download_url == null ||
                !asset.browser_download_url.StartsWith(prefix, StringComparison.Ordinal) ||
                !asset.browser_download_url.EndsWith("/" + asset.name, StringComparison.Ordinal) ||
                asset.digest == null || !Regex.IsMatch(asset.digest, @"^sha256:[a-fA-F0-9]{64}$"))
                throw new IOException("The " + product + " installer is missing valid download or SHA-256 information. Use the official releases page.");
            selected = asset;
        }
        if (selected == null) throw new IOException("The latest " + product + " release has no Windows setup executable. Use the official releases page.");
        return selected;
    }

    internal static Asset SelectInstaller(Release release, ToolSlot slot)
    {
        if (release == null || release.draft || release.prerelease || release.assets == null) throw new IOException("GitHub did not return a stable release for " + slot.name + ".");
        string prefix = "https://github.com/" + slot.repository + "/releases/download/";
        string pattern = "^" + Regex.Escape(slot.installerPattern).Replace("\\*", "[A-Za-z0-9._-]+") + "$";
        Asset selected = null;
        foreach (Asset asset in release.assets)
        {
            if (asset.name == null || !Regex.IsMatch(asset.name, pattern, RegexOptions.IgnoreCase)) continue;
            if (selected != null || asset.size <= 0 || asset.size > 4L * 1024 * 1024 * 1024 ||
                asset.browser_download_url == null || !asset.browser_download_url.StartsWith(prefix, StringComparison.Ordinal) ||
                !asset.browser_download_url.EndsWith("/" + asset.name, StringComparison.Ordinal) ||
                asset.digest == null || !Regex.IsMatch(asset.digest, @"^sha256:[a-fA-F0-9]{64}$"))
                throw new IOException("Release installer for " + slot.name + " is ambiguous or unverified.");
            selected = asset;
        }
        if (selected == null) throw new IOException("No matching verified setup found for " + slot.name + ".");
        return selected;
    }

    internal static string ReleasesPageFor(ToolSlot slot) { return "https://github.com/" + slot.repository + "/releases/latest"; }

    internal static Asset GetLatest(CancellationToken cancellation, ToolSlot slot)
    {
        cancellation.ThrowIfCancellationRequested();
        var request = Request("https://api.github.com/repos/" + slot.repository + "/releases/latest");
        using (cancellation.Register(request.Abort))
        using (var response = request.GetResponse())
        using (var input = response.GetResponseStream())
        using (var reader = new StreamReader(input))
            return SelectInstaller(new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Release>(reader.ReadToEnd()), slot);
    }

    internal static Asset GetLatest(CancellationToken cancellation, bool overlay = false)
    {
        cancellation.ThrowIfCancellationRequested();
        var request = Request(overlay ? OverlayApi : ReleaseApi);
        using (cancellation.Register(request.Abort))
        {
            try
            {
                using (var response = request.GetResponse())
                using (var input = response.GetResponseStream())
                using (var reader = new StreamReader(input))
                {
                    string json = reader.ReadToEnd(); cancellation.ThrowIfCancellationRequested();
                    return SelectInstaller(new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Release>(json), overlay);
                }
            }
            catch (WebException) { cancellation.ThrowIfCancellationRequested(); throw; }
        }
    }

    internal static string HashFile(string path, CancellationToken cancellation)
    {
        using (var input = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            byte[] buffer = new byte[1024 * 1024]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            { cancellation.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, count, buffer, 0); }
            sha.TransformFinalBlock(buffer, 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
    }

    internal static string DownloadLatest(string cache, CancellationToken cancellation, Action<int, string> progress, bool overlay = false)
    {
        string product = overlay ? "UFC 6 Parsectagon Overlay" : "PORTHEON";
        progress(0, "Checking latest " + product + " release...");
        var asset = GetLatest(cancellation, overlay);
        InstallLog.Write(product + " asset=" + asset.name + "; URL=" + asset.browser_download_url + "; SHA256=" + asset.digest);
        Directory.CreateDirectory(cache);
        string target = Path.Combine(cache, asset.name);
        string expectedHash = asset.digest.Substring(7);
        if (File.Exists(target) && new FileInfo(target).Length == asset.size &&
            String.Equals(HashFile(target, cancellation), expectedHash, StringComparison.OrdinalIgnoreCase))
        { progress(100, product + " installer verified."); return target; }
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cache)));
        if (drive.AvailableFreeSpace < asset.size + 100L * 1024 * 1024)
            throw new IOException("Not enough space to download " + product + ". Allow " + Program.Size(asset.size + 100L * 1024 * 1024) + " on " + drive.Name + ".");
        string temporary = Path.Combine(cache, ".setup-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            var request = Request(asset.browser_download_url);
            using (cancellation.Register(request.Abort))
            {
                try
                {
                    using (var response = request.GetResponse())
                    using (var input = response.GetResponseStream())
                    using (var output = File.Create(temporary))
                    {
                        byte[] buffer = new byte[256 * 1024]; long received = 0; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            cancellation.ThrowIfCancellationRequested(); received += count;
                            if (received > asset.size) throw new IOException(product + " download exceeded its published size.");
                            output.Write(buffer, 0, count);
                            progress((int)(received * 95 / asset.size), "Downloading " + product + ": " + Program.Size(received) + " / " + Program.Size(asset.size));
                        }
                        if (received != asset.size) throw new IOException("The " + product + " download was incomplete. Please retry.");
                    }
                }
                catch (WebException) { cancellation.ThrowIfCancellationRequested(); throw; }
            }
            progress(96, "Verifying " + product + " download...");
            if (!String.Equals(HashFile(temporary, cancellation), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException(product + " failed its SHA-256 check. The download will not be opened.");
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target);
            progress(100, product + " installer verified."); return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string DownloadLatest(string cache, CancellationToken cancellation, Action<int, string> progress, ToolSlot slot)
    {
        progress(0, "Checking latest " + slot.name + " release...");
        Asset asset = GetLatest(cancellation, slot);
        Directory.CreateDirectory(cache);
        string target = Path.Combine(cache, asset.name), expected = asset.digest.Substring(7);
        if (File.Exists(target) && new FileInfo(target).Length == asset.size && String.Equals(HashFile(target, cancellation), expected, StringComparison.OrdinalIgnoreCase)) return target;
        string temporary = Path.Combine(cache, ".setup-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            var request = Request(asset.browser_download_url);
            using (cancellation.Register(request.Abort))
            using (var response = request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = File.Create(temporary))
            {
                byte[] buffer = new byte[256 * 1024]; long received = 0; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellation.ThrowIfCancellationRequested(); received += count;
                    if (received > asset.size) throw new IOException("Download exceeded published size.");
                    output.Write(buffer, 0, count); progress((int)(received * 95 / asset.size), "Downloading " + slot.name + "...");
                }
                if (received != asset.size) throw new IOException("Incomplete download for " + slot.name + ".");
            }
            if (!String.Equals(HashFile(temporary, cancellation), expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("SHA-256 verification failed for " + slot.name + ".");
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(target)) File.Delete(target);
            File.Move(temporary, target); return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
