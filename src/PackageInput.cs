using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

internal sealed class PackageInput
{
    internal readonly string[] Files;
    internal readonly long Length;
    internal bool IsSplit { get { return Files.Length > 1; } }
    internal readonly string ArchivePath;

    private PackageInput(string[] files, long length, string archivePath) { Files = files; Length = length; ArchivePath = archivePath; }

    internal static PackageInput Open(string directory)
    {
        string single = Path.Combine(directory, "data.bin");
        if (File.Exists(single)) return new PackageInput(new[] { single }, new FileInfo(single).Length, single);
        string header = Path.Combine(directory, "data.bin.header");
        if (!File.Exists(header)) throw new FileNotFoundException("Place data.bin, or data.bin.header with every numbered data.bin part, beside setup.exe.");
        var numbered = new SortedDictionary<int, string>();
        foreach (string path in Directory.GetFiles(directory, "data.bin.*"))
        {
            Match match = Regex.Match(Path.GetFileName(path), @"\Adata\.bin\.(\d{3})\z", RegexOptions.IgnoreCase);
            if (match.Success) numbered[Int32.Parse(match.Groups[1].Value)] = path;
        }
        if (numbered.Count == 0) throw new FileNotFoundException("data.bin.header was found, but no numbered data.bin parts are present.");
        int expected = 1; long length = new FileInfo(header).Length; var files = new List<string> { header };
        foreach (var item in numbered)
        {
            if (item.Key != expected) throw new IOException("data.bin." + expected.ToString("D3") + " is missing.");
            long partLength = new FileInfo(item.Value).Length;
            if (partLength == 0) throw new IOException(Path.GetFileName(item.Value) + " is empty.");
            checked { length += partLength; }
            files.Add(item.Value); ++expected;
        }
        return new PackageInput(files.ToArray(), length, numbered[1]);
    }

    internal Stream OpenRead()
    {
        return IsSplit ? (Stream)new MultipartReadStream(Files, Length) : new FileStream(Files[0], FileMode.Open, FileAccess.Read, FileShare.Read);
    }

}

internal sealed class MultipartReadStream : Stream
{
    private readonly string[] files;
    private readonly long length;
    private int index;
    private readonly FileStream[] streams;
    private long position;
    internal MultipartReadStream(string[] files, long length)
    {
        this.files = files; this.length = length; streams = new FileStream[files.Length];
        try { for (int i = 0; i < files.Length; i++) streams[i] = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch { foreach (FileStream stream in streams) if (stream != null) stream.Dispose(); throw; }
    }
    public override bool CanRead { get { return true; } }
    public override bool CanSeek { get { return false; } }
    public override bool CanWrite { get { return false; } }
    public override long Length { get { return length; } }
    public override long Position { get { return position; } set { if (value != position && value != 0) throw new NotSupportedException(); if (value == 0 && position != 0) throw new NotSupportedException(); } }
    public override int Read(byte[] buffer, int offset, int count)
    {
        while (index < files.Length)
        {
            int read = streams[index].Read(buffer, offset, count);
            if (read != 0) { position += read; return read; }
            ++index;
        }
        return 0;
    }
    protected override void Dispose(bool disposing) { if (disposing) foreach (FileStream stream in streams) stream.Dispose(); base.Dispose(disposing); }
    public override void Flush() { throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
    public override void SetLength(long value) { throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
}
