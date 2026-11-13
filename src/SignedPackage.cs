using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal static class SignedPackage
{
    internal const int PrefixSize = 65536;
    internal static readonly byte[] Magic = Encoding.ASCII.GetBytes("UFC6PKG3");

    internal static byte[] Header(int metadataLength, int signatureLength, long payloadLength)
    {
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output))
        {
            writer.Write(Magic); writer.Write(3); writer.Write(metadataLength);
            writer.Write(signatureLength); writer.Write(PrefixSize); writer.Write(payloadLength);
            return output.ToArray();
        }
    }
    internal static byte[] Join(byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length); return result;
    }
    internal static RSACryptoServiceProvider Rsa(string xml)
    {
        var rsa = new RSACryptoServiceProvider(new CspParameters(24));
        rsa.PersistKeyInCsp = false;
        try { rsa.FromXmlString(xml); return rsa; } catch { rsa.Dispose(); throw; }
    }
    internal static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    internal static Package Read(string path, string publicKey)
    { using (var input = File.OpenRead(path)) return Read(input, publicKey); }

    internal static Package Read(Stream input, string publicKey)
    {
        input.Position = 0;
        byte[] prefix = new byte[PrefixSize]; int read = 0, count;
        while (read < prefix.Length && (count = input.Read(prefix, read, prefix.Length - read)) > 0) read += count;
        if (read != prefix.Length) throw new IOException("data.bin is incomplete or uses an older package format.");
        for (int i = 0; i < Magic.Length; i++) if (prefix[i] != Magic[i])
            throw new IOException("data.bin is not a supported signed package. Use the updated data.bin.");
        int version = BitConverter.ToInt32(prefix, 8), length = BitConverter.ToInt32(prefix, 12);
        int signatureLength = BitConverter.ToInt32(prefix, 16), prefixLength = BitConverter.ToInt32(prefix, 20);
        long payloadLength = BitConverter.ToInt64(prefix, 24);
        if (version != 3 || prefixLength != PrefixSize || length < 2 || length > 60 * 1024 ||
            signatureLength < 256 || signatureLength > 1024 || 32 + length + signatureLength > PrefixSize ||
            payloadLength < 32 || payloadLength > 16L * 1024 * 1024 * 1024 * 1024 || input.Length != PrefixSize + payloadLength)
            throw new IOException("data.bin has invalid lengths or is incomplete.");
        for (int i = 32 + length + signatureLength; i < PrefixSize; i++)
            if (prefix[i] != 0) throw new IOException("data.bin has an invalid package header.");
        byte[] signed = new byte[32 + length], signature = new byte[signatureLength], metadata = new byte[length];
        Buffer.BlockCopy(prefix, 0, signed, 0, signed.Length);
        Buffer.BlockCopy(prefix, signed.Length, signature, 0, signature.Length);
        Buffer.BlockCopy(prefix, 32, metadata, 0, metadata.Length);
        using (var rsa = Rsa(publicKey))
            if (signatureLength != rsa.KeySize / 8 || !rsa.VerifyData(signed, CryptoConfig.MapNameToOID("SHA256"), signature))
                throw new IOException("data.bin's package signature is not trusted or has been damaged.");
        Package package;
        try { package = new JavaScriptSerializer { MaxJsonLength = 60 * 1024 }.Deserialize<Package>(new UTF8Encoding(false, true).GetString(metadata)); }
        catch (Exception ex) { throw new IOException("data.bin has invalid package metadata.", ex); }
        const long maximum = 64L * 1024 * 1024 * 1024 * 1024;
        if (package == null || package.PayloadBytes != payloadLength || package.PayloadVersion != 2 ||
            String.IsNullOrWhiteSpace(package.Title) || package.Title.Length > 128 ||
            (package.GameVersion != null && package.GameVersion.Length > 64) ||
            package.UnpackedBytes < 0 || package.UnpackedBytes > maximum || package.FileCount < 1 || package.FileCount > 5000000 ||
            package.Sha256 == null || !Regex.IsMatch(package.Sha256, @"\A[a-fA-F0-9]{64}\z") ||
            package.AlterEgosArchiveBytes < 0 || package.AlterEgosArchiveBytes > payloadLength ||
            package.AlterEgosUnpackedBytes < 0 || package.AlterEgosUnpackedBytes > maximum ||
            package.AlterEgosInstalledDeltaBytes < -package.UnpackedBytes || package.AlterEgosInstalledDeltaBytes > maximum ||
            package.AlterEgosFileDelta < -package.FileCount || package.AlterEgosFileDelta > 5000000 ||
            (package.AlterEgosArchiveBytes > 0 && (package.AlterEgosArchivePath != "__ufc6_installer__\\alter-egos.rar" ||
                package.AlterEgosSha256 == null || !Regex.IsMatch(package.AlterEgosSha256, @"\A[a-fA-F0-9]{64}\z"))))
            throw new IOException("data.bin's package metadata is inconsistent or unsupported.");
        package.PayloadOffset = PrefixSize; package.ArchiveBytes = input.Length; package.ManifestSha256 = Hash(metadata);
        return package;
    }
}
