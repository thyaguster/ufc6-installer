using System;
using System.Diagnostics;
using System.Threading;

internal static class PerformanceTest
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--worker") { Thread.Sleep(20000); return 0; }
        if (InstallationPerformance.Mode != InstallationMode.Balanced) throw new Exception("Balanced is not the default.");
        var info = new ProcessStartInfo(typeof(PerformanceTest).Assembly.Location, "--worker") { UseShellExecute = false, CreateNoWindow = true };
        using (var process = Process.Start(info))
        using (var budget = InstallationPerformance.Attach(process))
        {
            try
            {
                foreach (var mode in new[] { InstallationMode.Balanced, InstallationMode.Quiet, InstallationMode.Fast })
                {
                    InstallationPerformance.Mode = mode;
                    uint flags, rate;
                    if (budget.ReadCpuRate(out flags, out rate))
                    {
                        uint expected = mode == InstallationMode.Quiet ? 2500u : 6000u;
                        if (mode == InstallationMode.Fast ? flags != 0 : flags != 5 || rate != expected)
                            throw new Exception("Windows job CPU control does not match " + mode + ": " + flags + "/" + rate);
                        Console.WriteLine("PASS: native Windows job updated live: " + mode + ", flags=" + flags + ", rate=" + rate);
                    }
                    else if (!InstallationPerformance.Hint.Contains("unavailable") && mode != InstallationMode.Fast)
                        throw new Exception("CPU cap failure is hidden from the UI.");
                    process.Refresh();
                    var expectedPriority = mode == InstallationMode.Fast ? ProcessPriorityClass.Normal : mode == InstallationMode.Quiet ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
                    if (process.PriorityClass != expectedPriority) throw new Exception("Worker priority mismatch.");
                }
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
        }
        InstallationPerformance.Mode = InstallationMode.Quiet;
        var pacer = new InstallationPerformance.VerificationPacer();
        var clock = Stopwatch.StartNew();
        var change = new Thread(delegate() { Thread.Sleep(100); InstallationPerformance.Mode = InstallationMode.Fast; });
        change.Start(); pacer.Pace(48 * 1024 * 1024, CancellationToken.None); change.Join();
        if (clock.Elapsed.TotalMilliseconds >= 700) throw new Exception("Verification did not respond to a live mode change.");
        var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { pacer.Pace(1024, cancellation.Token); throw new Exception("Cancelled verification kept running."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS: verification pacing reacts to live mode changes and cancellation.");
        return 0;
    }
}
