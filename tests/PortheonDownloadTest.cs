using System;
using System.IO;
using System.Threading;

internal static class PortheonDownloadTest
{
    private static PortheonDownloader.Release ValidRelease()
    {
        return new PortheonDownloader.Release { assets = new[] { new PortheonDownloader.Asset {
            name = "PORTHEON-9.9-Setup.exe", size = 123,
            digest = "sha256:" + new String('a', 64),
            browser_download_url = "https://github.com/XAndromedaXR/PORTHEON/releases/download/9.9/PORTHEON-9.9-Setup.exe"
        } } };
    }
    private static void Reject(PortheonDownloader.Release release, bool overlay = false)
    {
        try { PortheonDownloader.SelectInstaller(release, overlay); }
        catch (IOException) { return; }
        throw new Exception("Unsafe or unsupported release was accepted.");
    }
    private static int Main(string[] args)
    {
        var release = ValidRelease();
        if (PortheonDownloader.SelectInstaller(release).name != "PORTHEON-9.9-Setup.exe") throw new Exception("Valid asset was not selected.");
        release = ValidRelease(); release.prerelease = true; Reject(release);
        release = ValidRelease(); release.draft = true; Reject(release);
        release = ValidRelease(); release.assets[0].digest = null; Reject(release);
        release = ValidRelease(); release.assets[0].browser_download_url = "https://example.com/PORTHEON-9.9-Setup.exe"; Reject(release);
        release = ValidRelease(); release.assets[0].browser_download_url = release.assets[0].browser_download_url.Replace("https:", "http:"); Reject(release);
        release = ValidRelease(); release.assets[0].name = "../setup.exe"; Reject(release);
        release = ValidRelease(); release.assets = new[] { release.assets[0], release.assets[0] }; Reject(release);
        release = ValidRelease(); release.assets[0].size = 0; Reject(release);
        var overlayRelease = new PortheonDownloader.Release { assets = new[] { new PortheonDownloader.Asset {
            name = "UFC6-Actions-Overlay-Setup-0.9.18.exe", size = 123,
            digest = "sha256:" + new String('b', 64),
            browser_download_url = "https://github.com/thyaguster/ufc6-actions-overlay/releases/download/v0.9.18/UFC6-Actions-Overlay-Setup-0.9.18.exe"
        } } };
        if (PortheonDownloader.SelectInstaller(overlayRelease, true).name != overlayRelease.assets[0].name) throw new Exception("Valid overlay installer was not selected.");
        Reject(overlayRelease); // Overlay assets cannot be mistaken for PORTHEON.
        overlayRelease.assets[0].browser_download_url = "https://github.com/another-owner/ufc6-actions-overlay/releases/download/v0.9.18/UFC6-Actions-Overlay-Setup-0.9.18.exe";
        Reject(overlayRelease, true);
        Console.WriteLine("PASS: overlay asset selection is restricted to the author's repository and kept separate from PORTHEON.");
        string hashFixture = Path.Combine(args[0], "hash-fixture.txt");
        File.WriteAllText(hashFixture, "abc", new System.Text.UTF8Encoding(false));
        if (PortheonDownloader.HashFile(hashFixture, CancellationToken.None) != "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
            throw new Exception("SHA-256 check failed.");
        var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { PortheonDownloader.GetLatest(cancellation.Token); throw new Exception("Cancellation was ignored."); }
        catch (OperationCanceledException) { }
        if (PortheonDownloader.ParseOptionalToolsConfig("{\"portheonEnabled\":true}").portheonEnabled != true)
            throw new Exception("PORTHEON GitHub switch was not parsed.");
        try { PortheonDownloader.ParseOptionalToolsConfig("not json"); throw new Exception("Invalid optional-tools configuration was accepted."); }
        catch (IOException) { }
        Console.WriteLine("PASS: official URL, stable release, asset selection, checksum requirements, hashing and pre-request cancellation.");
        if (args.Length == 2 && (args[1] == "--live" || args[1] == "--live-overlay"))
        {
            string file = PortheonDownloader.DownloadLatest(args[0], CancellationToken.None, delegate(int p, string s) { }, args[1] == "--live-overlay");
            Console.WriteLine("PASS: downloaded latest official installer and verified published SHA-256: " + file);
            Console.WriteLine("Installer was NOT executed.");
        }
        return 0;
    }
}
