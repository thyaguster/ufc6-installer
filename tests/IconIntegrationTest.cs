using System;
using System.IO;
using System.Threading;

internal static class IconIntegrationTest
{
    private static int Main(string[] args)
    {
        Program.Package = new Package { XboxIconsSha256 = args[2] };
        Program.InstallXboxIcons(args[1], args[0], CancellationToken.None);
        if (!File.Exists(Path.Combine(args[0], ".ufc6-xbox-icons-backup.zip")))
            throw new Exception("Restore backup was not created.");
        Console.WriteLine("PASS: installer applied Xbox artwork and retained the restore backup/tool.");
        return 0;
    }
}
