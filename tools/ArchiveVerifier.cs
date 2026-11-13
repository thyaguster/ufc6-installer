using System;
using System.IO;
using System.Security.Cryptography;

public static class ArchiveVerifier
{
    private static string Hash(string path, long packedLength, out string packedHash)
    {
        using (var input = File.OpenRead(path))
        using (var whole = SHA256.Create())
        using (var packed = SHA256.Create())
        {
            byte[] buffer = new byte[4 * 1024 * 1024]; long offset = 0; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                whole.TransformBlock(buffer, 0, count, buffer, 0);
                long start = Math.Max(offset, 32), end = Math.Min(offset + count, 32 + packedLength);
                if (end > start) packed.TransformBlock(buffer, (int)(start - offset), (int)(end - start), buffer, (int)(start - offset));
                offset += count;
            }
            if (offset < 32 + packedLength) throw new IOException("Truncated package.");
            whole.TransformFinalBlock(buffer, 0, 0); packed.TransformFinalBlock(buffer, 0, 0);
            packedHash = BitConverter.ToString(packed.Hash).Replace("-", "");
            return BitConverter.ToString(whole.Hash).Replace("-", "");
        }
    }
    public static string VerifyRebundle(string original, string updated, long packedLength, string expectedOriginal)
    {
        string oldPacked, newPacked;
        string oldHash = Hash(original, packedLength, out oldPacked);
        if (!String.Equals(oldHash, expectedOriginal, StringComparison.OrdinalIgnoreCase)) throw new IOException("The original archive does not match its verified manifest.");
        string newHash = Hash(updated, packedLength, out newPacked);
        if (oldPacked != newPacked) throw new IOException("The original compressed game streams changed; the new package has not been accepted.");
        return newHash;
    }
}
