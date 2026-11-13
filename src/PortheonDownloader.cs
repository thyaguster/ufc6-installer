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

    internal static Asset SelectInstaller(Release release, bool overlay = false)
    {
        string product = overlay ? "UFC 6 Actions Overlay" : "PORTHEON";
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
        string product = overlay ? "UFC 6 Actions Overlay" : "PORTHEON";
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
}
