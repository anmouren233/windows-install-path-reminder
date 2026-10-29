using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace InstallPathReminder
{
    public sealed class InstallPathSuggestion
    {
        public string SoftwareName { get; private set; }
        public string FullPath { get; private set; }
        public bool NeedsReview { get; private set; }

        public static InstallPathSuggestion Create(string root, ProcessSnapshot process)
        {
            if (process == null) throw new ArgumentNullException("process");
            string normalized, error;
            if (!PathRules.TryValidate(root, out normalized, out error)) throw new ArgumentException(error);
            int available = Math.Min(80, 240 - normalized.Length - 1);
            if (available < 16) throw new ArgumentException("默认目录过长，请缩短目录后再生成软件安装路径。");
            string name = InferName(process);
            bool uncertain = String.IsNullOrWhiteSpace(name);
            if (uncertain) name = "待确认软件-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            name = SanitizeFolderName(name, available);
            if (String.IsNullOrWhiteSpace(name)) name = "待确认软件-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            return new InstallPathSuggestion { SoftwareName = name, FullPath = Path.Combine(normalized, name), NeedsReview = uncertain };
        }

        public static string InferName(ProcessSnapshot process)
        {
            bool msi = String.Equals(Path.GetFileName(process.Name ?? ""), "msiexec.exe", StringComparison.OrdinalIgnoreCase);
            if (msi)
            {
                string package = GetMsiPackagePath(process.CommandLine);
                string product = CleanName(MsiProductReader.Read(package), false);
                if (!IsGeneric(product)) return product;
                string packageName = CleanName(FileStem(package), true);
                return IsGeneric(packageName) ? "" : packageName;
            }
            string candidate = CleanName(process.ProductName, false);
            if (!IsGeneric(candidate)) return candidate;
            candidate = CleanName(process.FileDescription, false);
            if (!IsGeneric(candidate)) return candidate;
            candidate = CleanName(FileStem(process.Name), true);
            return IsGeneric(candidate) ? "" : candidate;
        }

        private static string FileStem(string value)
        {
            try { return Path.GetFileNameWithoutExtension(value ?? ""); }
            catch (ArgumentException) { return ""; }
        }

        internal static string GetMsiPackagePath(string command)
        {
            Match match = Regex.Match(command ?? "", @"(?:^|\s)[/-](?:package(?=\s)|[ia](?=\s|""|[a-z]:\\))\s*(?:""(?<path>[^""]+\.msi)""|(?<path>\S+\.msi))(?=\s|$)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["path"].Value : "";
        }

        private static string CleanName(string value, bool filename)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return "";
            if (IsGeneric(text)) return "";
            text = Regex.Replace(text, @"(?i)(?:user|system)?setup(?:32|64)?|installer(?=$|[-_.\s\d])|\b(?:installation wizard|installation launcher)\b|安装程序|安装向导|安装包|安装器", " ");
            text = Regex.Replace(text, @"(?i)^\s*(?:install(?:ation)?\s+(?:for\s+)?|安装\s*)", "");
            text = Regex.Replace(text, @"(?i)[\s_.-]*(?:x86|x64|amd64|arm64|win32|win64|32bit|64bit|32-bit|64-bit)(?=$|[\s_.-])", " ");
            if (filename)
            {
                text = Regex.Replace(text, @"(?i)[\s_.-]+(?:v?\d+(?:[._-]\d+)+(?:[\s_.-].*)?|v\d+|\d{4,})(?:$)", "");
                text = Regex.Replace(text, @"(?i)[\s_.-]+(?:zh[-_]cn|en[-_]us|win|windows|online|offline|full|latest|release)(?:$)", "");
                text = text.Replace('_', ' ');
            }
            text = Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '_', '.');
            return IsGeneric(text) ? "" : text;
        }

        private static bool IsGeneric(string value)
        {
            return String.IsNullOrWhiteSpace(value) || Regex.IsMatch(value.Trim(), @"^(?:setup|install|installer|installation|安装|安装程序|安装向导|安装包|msiexec|update|uninstall|setup/uninstall|Windows Installer|Microsoft Windows(?: Operating System)?|Inno Setup(?: Setup)?|Setup Factory.*|Nullsoft.*|NSIS.*|InstallShield.*|7-Zip SFX.*|SFX.*|Bootstrapper.*)$", RegexOptions.IgnoreCase);
        }

        internal static string SanitizeFolderName(string value, int maximum)
        {
            StringBuilder safe = new StringBuilder();
            foreach (char c in value ?? "")
            {
                if (Char.IsControl(c)) continue;
                safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }
            string name = Regex.Replace(safe.ToString(), @"\s+", " ").Trim(' ', '.');
            if (Regex.IsMatch(name, @"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
            if (name.Length > maximum)
            {
                int length = maximum;
                if (Char.IsHighSurrogate(name[length - 1])) length--;
                name = name.Substring(0, length).TrimEnd(' ', '.');
            }
            return name;
        }
    }

    internal static class MsiProductReader
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out uint database);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiDatabaseOpenViewW(uint database, string query, out uint view);
        [DllImport("msi.dll")] private static extern uint MsiViewExecute(uint view, uint record);
        [DllImport("msi.dll")] private static extern uint MsiViewFetch(uint view, out uint record);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiRecordGetStringW(uint record, uint field, StringBuilder value, ref uint length);
        [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);

        internal static string Read(string path)
        {
            uint database = 0, view = 0, record = 0;
            try
            {
                // Only open local MSI data, read-only. Never run installer actions.
                if (!Regex.IsMatch(path ?? "", @"^[a-z]:\\", RegexOptions.IgnoreCase) || !File.Exists(path)) return "";
                if (new DriveInfo(Path.GetPathRoot(path)).DriveType != DriveType.Fixed) return "";
                if (MsiOpenDatabaseW(path, IntPtr.Zero, out database) != 0) return "";
                if (MsiDatabaseOpenViewW(database, "SELECT `Value` FROM `Property` WHERE `Property`='ProductName'", out view) != 0) return "";
                if (MsiViewExecute(view, 0) != 0 || MsiViewFetch(view, out record) != 0) return "";
                uint length = 1023;
                StringBuilder value = new StringBuilder(1024);
                return MsiRecordGetStringW(record, 1, value, ref length) == 0 ? value.ToString() : "";
            }
            catch { return ""; }
            finally
            {
                if (record != 0) MsiCloseHandle(record);
                if (view != 0) MsiCloseHandle(view);
                if (database != 0) MsiCloseHandle(database);
            }
        }
    }
}
