using System;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class FinalMoveTest
{
    private static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static Exception Invoke(MethodInfo move, string source, string target, CancellationToken token)
    {
        try { move.Invoke(null, new object[] { source, target, token }); return null; }
        catch (TargetInvocationException ex) { return ex.InnerException; }
    }

    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "ufc6-final-move-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var assembly = Assembly.LoadFrom(args[0]);
            var method = assembly.GetType("Program").GetMethod("MoveStagingWithRetry", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(method != null, "Final move method is missing.");

            string source = Path.Combine(root, "locked-source"), target = Path.Combine(root, "installed");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "game.bin"), "game fixture");
            Exception failure = null;
            using (var stream = File.Open(Path.Combine(source, "game.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var worker = new Thread(() => failure = Invoke(method, source, target, CancellationToken.None));
                worker.Start();
                Thread.Sleep(3800);
                Assert(Directory.Exists(source) && !Directory.Exists(target), "Locked folder moved before the handle was released.");
                stream.Dispose();
                worker.Join();
            }
            Assert(failure == null && File.Exists(Path.Combine(target, "game.bin")), "Final move did not recover after the lock cleared: " + failure);

            string blocked = Path.Combine(root, "blocked-source");
            Directory.CreateDirectory(blocked);
            File.WriteAllText(Path.Combine(blocked, "game.bin"), "preserve me");
            Exception blockedFailure = Invoke(method, blocked, target, CancellationToken.None);
            Assert(blockedFailure is IOException && File.Exists(Path.Combine(blocked, "game.bin")), "A destination conflict lost the extracted files.");
            Console.WriteLine("PASS: a locked game folder is retried; a destination conflict leaves source files intact.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root) && root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
}
