using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal static class PackageTool
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static int Main(string[] args)
    {
        try
        {
            if (args[0] == "init") { Init(args[1], args[2]); return 0; }
            if (args[0] == "seal") { Seal(args[1], args[2], args[3], args[4], args[5]); return 0; }
            if (args[0] == "inspect")
            {
                Package package = SignedPackage.Read(args[1], File.ReadAllText(args[2]));
                Export(package, args[3]); return 0;
            }
            throw new ArgumentException("Unknown package-tool command.");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private static void Init(string privatePath, string publicPath)
    {
        if (File.Exists(publicPath)) return;
        if (File.Exists(privatePath)) throw new IOException("Signing key already exists but the public key is missing. Restore its matching public key.");
        Directory.CreateDirectory(Path.GetDirectoryName(privatePath));
        using (var rsa = new RSACryptoServiceProvider(3072, new CspParameters(24)))
        {
            rsa.PersistKeyInCsp = false;
            byte[] secret = Encoding.UTF8.GetBytes(rsa.ToXmlString(true));
            try { File.WriteAllBytes(privatePath, ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(secret, 0, secret.Length); }
            File.WriteAllText(publicPath, rsa.ToXmlString(false), new UTF8Encoding(false));
        }
        Console.WriteLine("Created a user-encrypted signing key and its public verification key.");
    }
    private static void Seal(string sourcePath, string outputPath, string manifestPath, string privatePath, string publicPath)
    {
        if (!File.Exists(privatePath)) throw new IOException("The private signing key is missing. Restore it to sign packages; setup-only builds need only the public key.");
        var package = Json.Deserialize<Package>(File.ReadAllText(manifestPath));
        string expectedHash = package.Sha256;
        string publicKey = File.ReadAllText(publicPath);
        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] magic = new byte[8]; source.Read(magic, 0, magic.Length); source.Position = 0;
            bool alreadySigned = Encoding.ASCII.GetString(magic) == "UFC6PKG3";
            long offset = 0;
            if (alreadySigned) offset = SignedPackage.Read(source, publicKey).PayloadOffset;
            package.PayloadBytes = source.Length - offset;
            source.Position = offset;
            using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var hash = new SHA256CryptoServiceProvider())
            {
                output.Write(new byte[SignedPackage.PrefixSize], 0, SignedPackage.PrefixSize);
                byte[] buffer = new byte[4 * 1024 * 1024]; int count, last = -1; long copied = 0;
                while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    output.Write(buffer, 0, count); hash.TransformBlock(buffer, 0, count, buffer, 0); copied += count;
                    int percent = (int)(copied * 100 / package.PayloadBytes);
                    if (percent / 10 != last) { last = percent / 10; Console.WriteLine("Writing signed package: " + percent + "%"); }
                }
                hash.TransformFinalBlock(buffer, 0, 0);
                package.Sha256 = BitConverter.ToString(hash.Hash).Replace("-", "");
                if (!String.IsNullOrEmpty(expectedHash) && !String.Equals(package.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The source payload does not match its saved SHA-256. The package has not been signed.");
                byte[] metadata = Encoding.UTF8.GetBytes(Json.Serialize(package));
                byte[] privateBytes = ProtectedData.Unprotect(File.ReadAllBytes(privatePath), null, DataProtectionScope.CurrentUser);
                try
                {
                    using (var rsa = SignedPackage.Rsa(Encoding.UTF8.GetString(privateBytes)))
                    {
                        if (rsa.ToXmlString(false) != publicKey) throw new IOException("The private key does not match the installer's public key.");
                        byte[] header = SignedPackage.Header(metadata.Length, rsa.KeySize / 8, package.PayloadBytes);
                        byte[] signature = rsa.SignData(SignedPackage.Join(header, metadata), CryptoConfig.MapNameToOID("SHA256"));
                        if (header.Length + metadata.Length + signature.Length > SignedPackage.PrefixSize) throw new IOException("Package metadata is too large.");
                        output.Position = 0; output.Write(header, 0, header.Length); output.Write(metadata, 0, metadata.Length); output.Write(signature, 0, signature.Length);
                    }
                }
                finally { Array.Clear(privateBytes, 0, privateBytes.Length); }
                output.Flush(true);
                Package checkedPackage = SignedPackage.Read(output, publicKey);
                Export(checkedPackage, manifestPath);
            }
        }
        Console.WriteLine("Signed package created; payload hash verified.");
    }
    private static void Export(Package package, string path)
    {
        var values = Json.Deserialize<Dictionary<string, object>>(Json.Serialize(package));
        values["ArchiveBytes"] = package.ArchiveBytes; values["PayloadOffset"] = package.PayloadOffset; values["PackageFormat"] = 3;
        File.WriteAllText(path, Json.Serialize(values), new UTF8Encoding(false));
    }
}
