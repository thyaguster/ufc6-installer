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
        string configJson = "{\"slots\":[{\"enabled\":true,\"name\":\"Another tool\",\"description\":\"A replacement\",\"note\":\"Run its setup\",\"repository\":\"example/project\",\"installerPattern\":\"Project-*.exe\"},{\"enabled\":false}]}";
        var config = PortheonDownloader.ParseOptionalToolsConfig(configJson);
        if (config.slots[0].repository != "example/project" || config.slots[1].enabled) throw new Exception("Configurable tool slots were not parsed.");
        string manualJson = "{\"slots\":[{\"enabled\":true,\"name\":\"Manual tool\",\"description\":\"Install from GitHub\",\"note\":\"Read the steps\",\"instructions\":\"Download its ZIP and follow its README.\",\"repository\":\"example/manual\",\"installMode\":\"github\"},{\"enabled\":false}]}";
        var manual = PortheonDownloader.ParseOptionalToolsConfig(manualJson);
        if (manual.slots[0].installMode != "github" || manual.slots[0].instructions.IndexOf("README", StringComparison.Ordinal) < 0)
            throw new Exception("Manual tool instructions were not parsed.");
        string configuredJson = configJson.Replace("\"installerPattern\":\"Project-*.exe\"", "\"installerPattern\":\"Project-*.exe\",\"installOrder\":15,\"installerArguments\":\"--target {gameFolder}\",\"detectedFile\":\"{gameFolder}\\\\Project.exe\"");
        var configured = PortheonDownloader.ParseOptionalToolsConfig(configuredJson);
        if (configured.slots[0].installOrder != 15 || configured.slots[0].installerArguments != "--target {gameFolder}" || configured.slots[0].detectedFile != @"{gameFolder}\Project.exe")
            throw new Exception("Configurable setup behavior was not parsed.");
        string folderJson = configJson.Replace("\"installerPattern\":\"Project-*.exe\"", "\"installerPattern\":\"Project-*.exe\",\"chooseFolder\":true,\"folderPrompt\":\"Choose a launcher folder\",\"installerArguments\":\"--folder {selectedFolder}\"");
        var folderConfig = PortheonDownloader.ParseOptionalToolsConfig(folderJson);
        if (!folderConfig.slots[0].chooseFolder || folderConfig.slots[0].folderPrompt != "Choose a launcher folder") throw new Exception("Folder choice was not parsed.");
        try { PortheonDownloader.ParseOptionalToolsConfig(folderJson.Replace("--folder {selectedFolder}", "--folder {gameFolder}")); throw new Exception("Folder prompt without selected-folder argument was accepted."); }
        catch (IOException) { }
        var alternateRelease = new PortheonDownloader.Release { assets = new[] { new PortheonDownloader.Asset { name="Project-1.exe", size=123, digest="sha256:"+new String('a',64), browser_download_url="https://github.com/example/project/releases/download/v1/Project-1.exe" } } };
        if (PortheonDownloader.SelectInstaller(alternateRelease, config.slots[0]).name != "Project-1.exe") throw new Exception("Replacement project was not accepted.");
        alternateRelease.assets[0].browser_download_url = "https://github.com/other/project/releases/download/v1/Project-1.exe";
        try { PortheonDownloader.SelectInstaller(alternateRelease, config.slots[0]); throw new Exception("Wrong repository was accepted."); } catch (IOException) { }
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
