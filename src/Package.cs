using System.Web.Script.Serialization;

internal sealed class Package
{
    public string Title { get; set; }
    public long UnpackedBytes { get; set; }
    [ScriptIgnore] public long ArchiveBytes { get; set; }
    public long PayloadBytes { get; set; }
    [ScriptIgnore] public long PayloadOffset { get; set; }
    [ScriptIgnore] public string ManifestSha256 { get; set; }
    public int FileCount { get; set; }
    public string Sha256 { get; set; }
    public int PayloadVersion { get; set; }
    public string AlterEgosArchivePath { get; set; }
    public string GameVersion { get; set; }
    public long AlterEgosArchiveBytes { get; set; }
    public long AlterEgosUnpackedBytes { get; set; }
    public long AlterEgosInstalledDeltaBytes { get; set; }
    public int AlterEgosFileDelta { get; set; }
    public string AlterEgosSha256 { get; set; }
    [ScriptIgnore] public long XboxIconsBytes { get; set; }
    [ScriptIgnore] public string XboxIconsSha256 { get; set; }
}

