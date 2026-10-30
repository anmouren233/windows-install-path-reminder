using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace InstallPathReminder
{
    public class AppSettings
    {
        public string PreferredPath { get; set; }
        public bool StartWithWindows { get; set; }
        public bool SoundEnabled { get; set; }
        public bool NotificationsEnabled { get; set; }
        public string ReminderChannel { get; set; }
        public List<string> IgnoredExecutables { get; set; }

        public AppSettings()
        {
            PreferredPath = PathRules.GetDefaultInstallPath();
            SoundEnabled = true;
            NotificationsEnabled = true;
            ReminderChannel = ReminderChannels.Popup;
            IgnoredExecutables = new List<string>();
        }
    }

    public class HistoryEntry
    {
        public string NotificationId { get; set; }
        public DateTime Time { get; set; }
        public string Application { get; set; }
        public string ExecutablePath { get; set; }
        public string Reason { get; set; }
        public string Action { get; set; }
        public string PreferredPath { get; set; }
        public string SuggestedPath { get; set; }
        public string SoftwareName { get; set; }
        public string NotificationDetails { get; set; }
    }

    public sealed class LocalStore
    {
        private readonly object gate = new object();
        public string DirectoryPath { get; private set; }
        public string SettingsFile { get { return Path.Combine(DirectoryPath, "settings.xml"); } }
        private string HistoryFile { get { return Path.Combine(DirectoryPath, "history.xml"); } }

        public LocalStore(string directory = null)
        {
            DirectoryPath = Path.GetFullPath(directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InstallPathReminder"));
        }

        public AppSettings LoadSettings()
        {
            lock (gate)
            {
                AppSettings result = ReadXml<AppSettings>(SettingsFile) ?? new AppSettings();
                result.ReminderChannel = ReminderChannels.Normalize(result.ReminderChannel);
                if (String.IsNullOrWhiteSpace(result.PreferredPath))
                    result.PreferredPath = PathRules.GetDefaultInstallPath();
                if (result.IgnoredExecutables == null)
                    result.IgnoredExecutables = new List<string>();
                result.IgnoredExecutables = result.IgnoredExecutables
                    .Where(x => !String.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToList();
                return result;
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            settings.ReminderChannel = ReminderChannels.Normalize(settings.ReminderChannel);
            lock (gate) WriteXml(SettingsFile, settings);
        }

        public List<HistoryEntry> LoadHistory()
        {
            lock (gate)
                return (ReadXml<List<HistoryEntry>>(HistoryFile) ?? new List<HistoryEntry>())
                    .Where(x => x != null).Take(200).ToList();
        }

        public void AddHistory(HistoryEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            lock (gate)
            {
                List<HistoryEntry> history = LoadHistory();
                history.Insert(0, new HistoryEntry {
                    NotificationId = XmlText(entry.NotificationId, 64),
                    Time = entry.Time == default(DateTime) ? DateTime.Now : entry.Time,
                    Application = XmlText(entry.Application, 512),
                    ExecutablePath = XmlText(entry.ExecutablePath, 32768),
                    Reason = XmlText(entry.Reason, 1024),
                    Action = XmlText(entry.Action, 512),
                    PreferredPath = XmlText(entry.PreferredPath, 32768),
                    SuggestedPath = XmlText(entry.SuggestedPath, 32768),
                    SoftwareName = XmlText(entry.SoftwareName, 512)
                });
                if (history.Count > 200) history.RemoveRange(200, history.Count - 200);
                WriteXml(HistoryFile, history);
            }
        }

        public void ClearHistory()
        {
            lock (gate) WriteXml(HistoryFile, new List<HistoryEntry>());
        }

        public HistoryEntry FindHistory(string notificationId)
        {
            if (String.IsNullOrWhiteSpace(notificationId)) return null;
            lock (gate) return LoadHistory().FirstOrDefault(x => String.Equals(x.NotificationId, notificationId, StringComparison.OrdinalIgnoreCase));
        }

        public void UpdateHistoryAction(string notificationId, string action, string details = null)
        {
            lock (gate)
            {
                List<HistoryEntry> history = LoadHistory();
                HistoryEntry entry = history.FirstOrDefault(x => String.Equals(x.NotificationId, notificationId, StringComparison.OrdinalIgnoreCase));
                if (entry != null) { entry.Action = XmlText(action, 512); if (details != null) entry.NotificationDetails = XmlText(details, 4096); WriteXml(HistoryFile, history); }
            }
        }

        private static string XmlText(string value, int limit)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < value.Length && result.Length < limit; i++)
            {
                char c = value[i];
                if (XmlConvert.IsXmlChar(c)) result.Append(c);
                else if (Char.IsHighSurrogate(c) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1]))
                {
                    if (result.Length + 2 > limit) break;
                    result.Append(c); result.Append(value[++i]);
                }
            }
            return result.ToString();
        }

        private T ReadXml<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                XmlReaderSettings options = new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = 4 * 1024 * 1024
                };
                using (XmlReader reader = XmlReader.Create(path, options))
                    return new XmlSerializer(typeof(T)).Deserialize(reader) as T;
            }
            catch (InvalidOperationException) { BackupCorrupt(path); return null; }
            catch (XmlException) { BackupCorrupt(path); return null; }
        }

        private static void BackupCorrupt(string path)
        {
            try
            {
                string backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                File.Move(path, backup);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void WriteXml<T>(string path, T value)
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    XmlWriterSettings options = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false };
                    using (XmlWriter writer = XmlWriter.Create(file, options))
                        new XmlSerializer(typeof(T)).Serialize(writer, value);
                    file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static class PathRules
    {
        public static string GetDefaultInstallPath()
        {
            string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (String.IsNullOrEmpty(systemRoot)) systemRoot = @"C:\";
            foreach (DriveInfo drive in DriveInfo.GetDrives().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady &&
                        !String.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase) &&
                        drive.AvailableFreeSpace > 1024L * 1024L * 1024L)
                        return Path.Combine(drive.Name, "Apps");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return Path.Combine(systemRoot, "Apps");
        }

        public static bool TryValidate(string input, out string normalized, out string error)
        {
            normalized = String.Empty;
            error = String.Empty;
            if (String.IsNullOrWhiteSpace(input)) { error = "请填写推荐安装目录。"; return false; }
            string candidate = input.Trim().Replace('/', '\\');
            if (!Regex.IsMatch(candidate, @"^[A-Za-z]:\\"))
            { error = @"请使用本地磁盘的完整路径，例如 D:\Apps。"; return false; }
            if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || candidate.IndexOfAny(new[] { '*', '?', '"', '<', '>', '|' }) >= 0)
            { error = "路径中包含无效字符。"; return false; }
            try
            {
                string[] segments = candidate.Substring(3).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string segment in segments)
                {
                    if (segment == "." || segment == "..")
                    { error = "请填写明确的目录，不要使用 . 或 ..。"; return false; }
                    if (segment.EndsWith(" ") || segment.EndsWith(".") || segment.IndexOf(':') >= 0 || segment.Length > 255 ||
                        Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                    { error = "目录名称包含 Windows 不支持的名称或结尾字符。"; return false; }
                }
                candidate = Path.GetFullPath(candidate);
                string root = Path.GetPathRoot(candidate);
                candidate = candidate.TrimEnd('\\');
                if (String.Equals(candidate, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                { error = "请选择磁盘中的文件夹，不能直接使用磁盘根目录。"; return false; }
                DriveInfo drive = new DriveInfo(root);
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                { error = "请选择已连接的本地固定磁盘。"; return false; }
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
                if (!String.IsNullOrEmpty(windows) && (String.Equals(candidate, windows, StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)))
                { error = "不能将 Windows 系统目录作为推荐安装目录。"; return false; }
                if (File.Exists(candidate)) { error = "该路径是一个文件，请选择文件夹。"; return false; }
                normalized = candidate;
                return true;
            }
            catch (ArgumentException) { error = "路径格式无效。"; return false; }
            catch (NotSupportedException) { error = "路径格式不受支持。"; return false; }
            catch (IOException) { error = "无法访问此磁盘，或路径过长。"; return false; }
            catch (UnauthorizedAccessException) { error = "没有权限访问此路径。"; return false; }
        }
    }

    public static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "InstallPathReminder";
        private static string Command { get { return "\"" + Application.ExecutablePath + "\" --background"; } }

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return key != null && String.Equals(key.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasOwnedEntry()
        {
            return GetOwnedCommand() != null;
        }

        public static string GetOwnedCommand()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                string command = key == null ? null : key.GetValue(ValueName) as string;
                return IsOwnedCommand(command) ? command : null;
            }
        }

        internal static bool IsOwnedCommand(string command)
        {
            if (String.IsNullOrEmpty(command)) return false;
            const string suffix = "\" --background";
            if (command.Length <= suffix.Length + 1 || !command.StartsWith("\"") ||
                !command.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            string path = command.Substring(1, command.Length - suffix.Length - 1);
            if (path.IndexOf('"') >= 0 || !Regex.IsMatch(path, @"^[A-Za-z]:\\")) return false;
            try
            {
                return String.Equals(Path.GetFileName(path), Path.GetFileName(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }

        public static void RestoreOwnedCommand(string command)
        {
            if (command != null && !IsOwnedCommand(command))
                throw new ArgumentException("只能恢复本程序的启动命令。", "command");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new IOException("无法访问当前用户的开机启动设置。");
                object existingValue = key.GetValue(ValueName);
                string existing = existingValue as string;
                if (existingValue != null && !IsOwnedCommand(existing))
                    throw new InvalidOperationException("同名启动项已被其他程序占用，未更改该启动项。");
                if (command != null) key.SetValue(ValueName, command, RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            RestoreOwnedCommand(enabled ? Command : null);
        }
    }
}
