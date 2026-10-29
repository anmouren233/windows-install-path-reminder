using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using InstallPathReminder;

internal static class MonitorSmoke
{
    private static readonly object Gate = new object();
    private static readonly List<string> Detected = new List<string>();
    private static readonly List<Process> Children = new List<Process>();
    private static string directory;
    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static int Count { get { lock (Gate) return Detected.Count; } }

    private static Process Start(string name)
    {
        string path = Path.Combine(directory, name);
        string fixture = Path.Combine(directory, "Fixture.exe");
        if (!File.Exists(path)) File.Copy(fixture, path);
        Process process = Process.Start(new ProcessStartInfo(path, "18") { UseShellExecute = false, CreateNoWindow = true });
        Children.Add(process);
        return process;
    }

    private static bool WaitCount(int count, int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            if (Count >= count) return true;
            Thread.Sleep(100);
        }
        return Count >= count;
    }

    private static int Main(string[] args)
    {
        directory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(directory);
        try
        {
            Start("BaselineSetup.exe");
            using (ProcessMonitor monitor = new ProcessMonitor())
            {
                monitor.StatusChanged += delegate(string status) { Console.WriteLine("STATUS: " + status); };
                monitor.InstallerDetected += delegate(ProcessSnapshot snapshot, string reason)
                {
                    lock (Gate) { Detected.Add(snapshot.Name); }
                    Console.WriteLine("DETECTED: " + snapshot.Name + " / " + reason);
                };
                monitor.Start();
                Console.WriteLine("MODE: " + monitor.Mode);
                Thread.Sleep(1400);
                Check(Count == 0, "No startup alert for existing process");
                Start("FreshSetup.exe");
                Check(WaitCount(1, 6500), "New installer detected within 6.5 seconds");
                Start("FreshSetup.exe");
                Start("OrdinaryApp.exe");
                Start("WidgetUninstall.exe");
                Start("WidgetUpdater.exe");
                Thread.Sleep(1800);
                Check(Count == 1, "Repeated same-path installer deduplicated; ordinary/updater/uninstaller excluded");
                monitor.Paused = true;
                Start("PausedSetup.exe");
                Thread.Sleep(1800);
                Check(Count == 1, "Paused monitor emits no reminder");
                monitor.Paused = false;
                Thread.Sleep(1400);
                Check(Count == 1, "Resume does not replay processes started while paused");
                Start("ResumedSetup.exe");
                Check(WaitCount(2, 6500), "New installer detected after resume");
            }
            Thread.Sleep(400);
            int afterDispose = Count;
            Start("DisposedSetup.exe");
            Thread.Sleep(1300);
            Check(Count == afterDispose, "Disposed monitor emits no reminder");
            ProcessMonitor disposedFirst = new ProcessMonitor();
            disposedFirst.Dispose();
            try { disposedFirst.Start(); }
            catch (ObjectDisposedException) { }
            disposedFirst.Dispose();
            Check(true, "Dispose before Start handled safely");
            Console.WriteLine("TOTAL PASS: " + checks);
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally
        {
            foreach (Process process in Children)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                process.Dispose();
            }
        }
    }
}
