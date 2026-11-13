using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

internal enum InstallationMode { Fast, Balanced, Quiet }

internal static class InstallationPerformance
{
    private static readonly object Sync = new object();
    private static readonly List<WorkerBudget> Workers = new List<WorkerBudget>();
    private static InstallationMode mode = InstallationMode.Balanced;
    private static bool capUnavailable;
    internal static InstallationMode Mode
    {
        get { lock (Sync) return mode; }
        set
        {
            lock (Sync)
            {
                mode = value; capUnavailable = false;
                foreach (var worker in Workers) worker.Apply(value);
                InstallLog.Write("Installation mode changed to " + value);
            }
        }
    }
    internal static string Hint
    {
        get
        {
            lock (Sync)
            {
                if (mode == InstallationMode.Fast) return "No worker CPU cap";
                if (capUnavailable) return "Priority only · CPU cap unavailable";
                return mode == InstallationMode.Quiet ? "25% worker CPU cap · Low priority" : "60% worker CPU cap";
            }
        }
    }
    internal static WorkerBudget Attach(Process process)
    {
        lock (Sync)
        {
            var worker = new WorkerBudget(process);
            Workers.Add(worker); return worker;
        }
    }

    internal sealed class WorkerBudget : IDisposable
    {
        private readonly Process process;
        private IntPtr job;
        private bool disposed;
        internal WorkerBudget(Process process)
        {
            this.process = process;
            try
            {
                if (process.HasExited) return;
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                Apply(Mode);
                if (!AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
                if (!Exited()) { capUnavailable = true; InstallLog.Write("Worker CPU cap unavailable; using priority only: " + ex.Message); }
            }
            ApplyPriority(Mode);
        }
        private bool Exited() { try { return process.HasExited; } catch (InvalidOperationException) { return true; } }
        private void ApplyPriority(InstallationMode selected)
        {
            try
            {
                if (!Exited()) process.PriorityClass = selected == InstallationMode.Fast ? ProcessPriorityClass.Normal :
                    selected == InstallationMode.Quiet ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
            }
            catch (Win32Exception ex) { InstallLog.Write("Worker priority could not change: " + ex.Message); }
            catch (InvalidOperationException) { }
        }
        internal void Apply(InstallationMode selected)
        {
            if (disposed || Exited()) return;
            ApplyPriority(selected);
            if (job == IntPtr.Zero) { capUnavailable = true; return; }
            var rate = new CpuRate { Flags = selected == InstallationMode.Fast ? 0u : 5u,
                Rate = selected == InstallationMode.Fast ? 10000u : selected == InstallationMode.Quiet ? 2500u : 6000u };
            if (!SetInformationJobObject(job, 15, ref rate, (uint)Marshal.SizeOf(typeof(CpuRate))))
            {
                capUnavailable = true;
                InstallLog.Write("Worker CPU rate control failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
        }
        internal bool ReadCpuRate(out uint flags, out uint rate)
        {
            CpuRate result = new CpuRate();
            bool success = job != IntPtr.Zero && QueryInformationJobObject(job, 15, out result, (uint)Marshal.SizeOf(typeof(CpuRate)), IntPtr.Zero);
            if (!success) { flags = rate = 0; return false; }
            flags = result.Flags; rate = result.Rate; return true;
        }
        public void Dispose()
        {
            lock (Sync)
            {
                if (disposed) return;
                disposed = true; Workers.Remove(this);
                if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
            }
        }
    }

    internal sealed class VerificationPacer
    {
        private InstallationMode previous = (InstallationMode)(-1);
        private double due;
        internal void Pace(int bytes, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            InstallationMode selected = Mode;
            double now = Stopwatch.GetTimestamp();
            if (selected != previous) { previous = selected; due = now; }
            int mibPerSecond = selected == InstallationMode.Fast ? 0 : selected == InstallationMode.Quiet ? 48 : 192;
            if (mibPerSecond == 0) { due = now; return; }
            due = Math.Max(due, now - Stopwatch.Frequency / 4.0) + bytes * (double)Stopwatch.Frequency / (mibPerSecond * 1048576.0);
            while (due > Stopwatch.GetTimestamp())
            {
                cancellation.ThrowIfCancellationRequested();
                if (Mode != selected) { previous = (InstallationMode)(-1); return; }
                double milliseconds = (due - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                if (milliseconds <= 0) break;
                if (cancellation.WaitHandle.WaitOne((int)Math.Min(50, Math.Max(1, milliseconds)))) cancellation.ThrowIfCancellationRequested();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CpuRate { internal uint Flags; internal uint Rate; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref CpuRate information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out CpuRate information, uint size, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
