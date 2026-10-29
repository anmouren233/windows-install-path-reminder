using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

namespace InstallPathReminder
{
    public sealed class ProcessSnapshot
    {
        public int ProcessId;
        public int ParentProcessId;
        public int SessionId;
        public string Name = "";
        public string ExecutablePath = "";
        public string CommandLine = "";
        public string FileDescription = "";
        public string ProductName = "";
        internal long StartTimeUtcTicks;
    }

    public sealed class DetectionResult
    {
        public bool IsInstaller;
        public string Reason = "";
    }

    // A heuristic, not a security boundary. Never reads or changes installer settings.
    public static class InstallerClassifier
    {
        private static readonly Regex ExcludedName = new Regex(
            @"uninstall|unins\d*|updater?|autoupdate|卸载|更新|^(?:installutil|trustedinstaller|tiworker|setuphost|setupprep|setupplatform|dism|pkgmgr|wusa)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex InstallerName = new Regex(
            @"(?:^|[-_.\s])(?:setup|install|installer|installation)(?:$|[-_.\s\d])|(?:setup|installer)(?:$|[-_.\s\d])|安装",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex InstallerMetadata = new Regex(
            @"\b(?:setup|installer|installation wizard|installation launcher)\b|安装(?:程序|向导|包|器)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MsiNegative = new Regex(
            @"(?:^|\s)[/-](?:x|p|y|z|v)(?=\s|""|\{|[a-z]:\\|\\\\|$)|(?:^|\s)[/-](?:uninstall|update)(?=\s|""|$)|(?:^|\s)[/-]f[a-z]*(?=\s|""|\{|[a-z]:\\|\\\\|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MsiPositive = new Regex(
            @"(?:^|\s)[/-](?:i|a)(?=\s|""|\{|[a-z]:\\|\\\\)|(?:^|\s)[/-]package(?=\s|"")",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static DetectionResult Classify(ProcessSnapshot process)
        {
            if (process == null) return No();
            string name = process.Name ?? "";
            try { name = Path.GetFileNameWithoutExtension(name); } catch (ArgumentException) { return No(); }
            if (name.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                string command = process.CommandLine ?? "";
                if (MsiNegative.IsMatch(command)) return No();
                return MsiPositive.IsMatch(command) ? Yes("Windows Installer 安装命令") : No();
            }
            if (ExcludedName.IsMatch(name)) return No();
            if (HasMaintenanceSwitch(process.CommandLine ?? "")) return No();
            // Metadata exclusions prevent a generic setup.exe used as an updater/uninstaller.
            string description = process.FileDescription ?? "";
            if (Regex.IsMatch(description, @"\b(?:uninstaller|updater)\b|卸载|更新程序", RegexOptions.IgnoreCase)) return No();
            if (InstallerName.IsMatch(name)) return Yes("文件名含安装程序特征");
            // A description that merely mentions C:\Setup\... is not an installer.
            // Discard path tails before examining descriptive words.
            string metadataText = Regex.Replace(description, @"(?:[a-z]:)?[\\/][^\r\n]*", "", RegexOptions.IgnoreCase);
            if (InstallerMetadata.IsMatch(metadataText)) return Yes("文件描述含安装程序特征");
            return No();
        }

        private static bool HasMaintenanceSwitch(string command)
        {
            // Match only complete, unquoted switches. Quoted filenames or argument values
            // can contain words such as " /update ", without changing the operation mode.
            int position = 0;
            while (position < command.Length)
            {
                while (position < command.Length && Char.IsWhiteSpace(command[position])) position++;
                int start = position;
                bool inQuotes = false;
                bool containsQuote = false;
                int backslashes = 0;
                while (position < command.Length)
                {
                    char current = command[position];
                    if (!inQuotes && Char.IsWhiteSpace(current)) break;
                    if (current == '"')
                    {
                        containsQuote = true;
                        if ((backslashes % 2) == 0) inQuotes = !inQuotes;
                    }
                    backslashes = current == '\\' ? backslashes + 1 : 0;
                    position++;
                }
                if (containsQuote || position == start) continue;
                string token = command.Substring(start, position - start);
                if (token.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("/repair", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("--repair", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("/update", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("--update", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static DetectionResult Yes(string reason) { return new DetectionResult { IsInstaller = true, Reason = reason }; }
        private static DetectionResult No() { return new DetectionResult(); }
    }

    public sealed class ProcessMonitor : IDisposable
    {
        private sealed class ProcessState
        {
            public ProcessSnapshot Snapshot;
            public DateTime LastSeen;
            public bool Active;
            public bool InstallerFamily;
        }

        private readonly object gate = new object();
        private readonly object lifecycleGate = new object();
        private readonly Dictionary<int, ProcessState> states = new Dictionary<int, ProcessState>();
        private readonly Dictionary<string, DateTime> recent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly int sessionId = Process.GetCurrentProcess().SessionId;
        private readonly int ownId = Process.GetCurrentProcess().Id;
        private ManagementEventWatcher watcher;
        private Timer timer;
        private volatile bool disposed;
        private volatile bool paused;
        private bool started;
        private int scanning;
        private long startFileTime;
        private volatile string mode = "尚未启动";

        public event Action<ProcessSnapshot, string> InstallerDetected;
        public event Action<string> StatusChanged;
        public bool Paused { get { return paused; } set { paused = value; } }
        public string Mode { get { return mode; } }

        public void Start()
        {
            string initialMode;
            // Serialize resource creation with Dispose, including an early exit during startup.
            lock (lifecycleGate)
            {
                if (disposed) return;
                if (started) return;
                started = true;
                startFileTime = DateTime.UtcNow.ToFileTimeUtc();
                Scan(true);
                try
                {
                    watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace WHERE SessionID = " + sessionId));
                    watcher.EventArrived += OnProcessStarted;
                    watcher.Stopped += delegate { if (!disposed) SetMode("兼容轮询（约 1 秒）"); };
                    watcher.Start();
                    initialMode = "实时监测 + 1 秒补漏";
                }
                catch (Exception ex)
                {
                    if (!(ex is ManagementException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.Runtime.InteropServices.COMException || ex is InvalidOperationException)) throw;
                    if (watcher != null) { watcher.Dispose(); watcher = null; }
                    initialMode = "兼容轮询（约 1 秒）";
                }
                timer = new Timer(delegate { Scan(false); }, null, 0, 1000);
            }
            SetMode(initialMode);
        }

        private void SetMode(string value)
        {
            mode = value;
            Action<string> handler = StatusChanged;
            if (handler != null && !disposed) { try { handler(value); } catch { } }
        }

        private static ProcessSnapshot ReadBasic(Process process)
        {
            try
            {
                ProcessSnapshot info = new ProcessSnapshot { ProcessId = process.Id, SessionId = process.SessionId, Name = process.ProcessName + ".exe" };
                try { info.StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks; } catch { }
                return info;
            }
            catch { return null; }
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs args)
        {
            if (disposed) return;
            try
            {
                int pid = Convert.ToInt32(args.NewEvent["ProcessID"]);
                int sid = Convert.ToInt32(args.NewEvent["SessionID"]);
                if (sid != sessionId || pid == ownId) return;
                long eventTime = Convert.ToInt64(args.NewEvent["TIME_CREATED"]);
                if (eventTime <= startFileTime) return;
                ProcessSnapshot info = null;
                try { using (Process p = Process.GetProcessById(pid)) { info = ReadBasic(p); } } catch { }
                if (info == null) info = new ProcessSnapshot { ProcessId = pid, SessionId = sid, Name = Convert.ToString(args.NewEvent["ProcessName"]) };
                if (info.SessionId != sessionId) return;
                info.ParentProcessId = Convert.ToInt32(args.NewEvent["ParentProcessID"]);
                Consider(info, false, true);
            }
            catch { /* A process may disappear before its event can be inspected. */ }
        }

        private void Scan(bool baseline)
        {
            if (disposed || Interlocked.Exchange(ref scanning, 1) == 1) return;
            try
            {
                DateTime now = DateTime.UtcNow;
                HashSet<int> alive = new HashSet<int>();
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        ProcessSnapshot info = ReadBasic(p);
                        if (info == null || info.SessionId != sessionId || info.ProcessId == ownId) continue;
                        alive.Add(info.ProcessId);
                        Consider(info, baseline, false);
                    }
                }
                lock (gate)
                {
                    List<int> remove = new List<int>();
                    foreach (KeyValuePair<int, ProcessState> pair in states)
                    {
                        if (!alive.Contains(pair.Key)) pair.Value.Active = false;
                        if (!pair.Value.Active && now - pair.Value.LastSeen > TimeSpan.FromMinutes(2)) remove.Add(pair.Key);
                    }
                    foreach (int pid in remove) states.Remove(pid);
                    List<string> stale = new List<string>();
                    foreach (KeyValuePair<string, DateTime> pair in recent) if (now - pair.Value > TimeSpan.FromSeconds(45)) stale.Add(pair.Key);
                    foreach (string key in stale) recent.Remove(key);
                }
            }
            catch { /* Monitoring continues on the next tick. */ }
            finally { Interlocked.Exchange(ref scanning, 0); }
        }

        private void Consider(ProcessSnapshot info, bool baseline, bool fromEvent)
        {
            ProcessState state;
            bool skip;
            lock (gate)
            {
                if (disposed) return;
                ProcessState existing;
                if (states.TryGetValue(info.ProcessId, out existing))
                {
                    bool same = info.StartTimeUtcTicks != 0 && existing.Snapshot.StartTimeUtcTicks != 0
                        ? info.StartTimeUtcTicks == existing.Snapshot.StartTimeUtcTicks
                        : existing.Active && !fromEvent;
                    // A WMI event can arrive just after the polling scan found this process.
                    if (!same && fromEvent && existing.Active && existing.Snapshot.StartTimeUtcTicks == 0 && info.StartTimeUtcTicks == 0)
                        same = DateTime.UtcNow - existing.LastSeen < TimeSpan.FromSeconds(2);
                    if (same)
                    {
                        existing.Active = true;
                        existing.LastSeen = DateTime.UtcNow;
                        if (info.ParentProcessId != 0) existing.Snapshot.ParentProcessId = info.ParentProcessId;
                        return;
                    }
                }
                state = new ProcessState { Snapshot = info, LastSeen = DateTime.UtcNow, Active = true };
                states[info.ProcessId] = state;
                skip = baseline || paused;
            }
            if (!skip) ThreadPool.QueueUserWorkItem(delegate { Inspect(state); });
        }

        private void Inspect(ProcessState state)
        {
            if (disposed || paused) return;
            ProcessSnapshot info = state.Snapshot;
            // Read only the new process; never enumerate all command lines or retain them on disk.
            try
            {
                using (ManagementObject item = new ManagementObject("Win32_Process.Handle='" + info.ProcessId + "'"))
                {
                    item.Get();
                    if (Convert.ToInt32(item["SessionId"]) != sessionId) return;
                    string creation = Convert.ToString(item["CreationDate"]);
                    if (!String.IsNullOrEmpty(creation) && info.StartTimeUtcTicks != 0)
                    {
                        long actual = ManagementDateTimeConverter.ToDateTime(creation).ToUniversalTime().Ticks;
                        // WMI CreationDate has microsecond precision; tolerate its rounding.
                        if (Math.Abs(actual - info.StartTimeUtcTicks) > TimeSpan.TicksPerMillisecond) return;
                    }
                    info.CommandLine = Convert.ToString(item["CommandLine"]) ?? "";
                    info.ExecutablePath = Convert.ToString(item["ExecutablePath"]) ?? "";
                    info.ParentProcessId = Convert.ToInt32(item["ParentProcessId"]);
                }
            }
            catch { }
            if (String.IsNullOrEmpty(info.ExecutablePath))
            {
                try
                {
                    using (Process p = Process.GetProcessById(info.ProcessId))
                    {
                        ProcessSnapshot current = ReadBasic(p);
                        if (current == null || current.SessionId != sessionId) return;
                        if (info.StartTimeUtcTicks != 0 && current.StartTimeUtcTicks != 0 && info.StartTimeUtcTicks != current.StartTimeUtcTicks) return;
                        info.ExecutablePath = p.MainModule.FileName;
                    }
                }
                catch { }
            }
            if (!String.IsNullOrEmpty(info.ExecutablePath))
            {
                try
                {
                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(info.ExecutablePath);
                    info.FileDescription = version.FileDescription ?? "";
                    info.ProductName = version.ProductName ?? "";
                }
                catch { }
            }
            DetectionResult result = InstallerClassifier.Classify(info);
            lock (gate)
            {
                ProcessState current;
                if (disposed || paused || !states.TryGetValue(info.ProcessId, out current) || !Object.ReferenceEquals(current, state)) return;
                if (HasInstallerAncestor(state)) { state.InstallerFamily = true; return; }
                if (!result.IsInstaller) return;
                state.InstallerFamily = true;
                string key = DedupKey(info);
                DateTime last;
                if (recent.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(45)) return;
                recent[key] = DateTime.UtcNow;
            }
            Action<ProcessSnapshot, string> handler = InstallerDetected;
            if (handler != null && !disposed && !paused) { try { handler(info, result.Reason); } catch { } }
        }

        private bool HasInstallerAncestor(ProcessState child)
        {
            HashSet<int> visited = new HashSet<int>();
            ProcessState cursor = child;
            for (int depth = 0; depth < 16; depth++)
            {
                int parentId = cursor.Snapshot.ParentProcessId;
                ProcessState parent;
                if (parentId == 0 || !visited.Add(parentId) || !states.TryGetValue(parentId, out parent)) return false;
                // Never treat a recycled parent PID as the ancestor of an older child.
                if (cursor.Snapshot.StartTimeUtcTicks != 0 && parent.Snapshot.StartTimeUtcTicks != 0 && parent.Snapshot.StartTimeUtcTicks > cursor.Snapshot.StartTimeUtcTicks) return false;
                if (parent.InstallerFamily) return true;
                cursor = parent;
            }
            return false;
        }

        private static string DedupKey(ProcessSnapshot info)
        {
            string path = String.IsNullOrEmpty(info.ExecutablePath) ? info.Name : info.ExecutablePath;
            if (String.Equals(info.Name, "msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                Match package = Regex.Match(info.CommandLine ?? "", "\"([^\"]+\\.msi)\"|(\\S+\\.msi)", RegexOptions.IgnoreCase);
                return path + "|" + (package.Success ? package.Value : (info.CommandLine ?? "").Trim());
            }
            return path;
        }

        public void Dispose()
        {
            lock (lifecycleGate)
            {
                if (disposed) return;
                disposed = true;
                if (timer != null) timer.Dispose();
                if (watcher != null)
                {
                    try { watcher.Stop(); } catch { }
                    watcher.Dispose();
                }
            }
        }
    }
}
