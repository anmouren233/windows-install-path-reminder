using System;
using System.IO;
using System.Linq;
using InstallPathReminder;

internal static class StorageTests
{
    private static int count;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        count++;
        Console.WriteLine("PASS: " + name);
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Provide an isolated workspace test directory.");
            string root = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(root);
            LocalStore store = new LocalStore(Path.Combine(root, Guid.NewGuid().ToString("N")));
            AppSettings defaults = store.LoadSettings();
            Check(defaults.SoundEnabled && defaults.NotificationsEnabled && !defaults.StartWithWindows, "safe first-run defaults");
            Check(defaults.ReminderChannel == ReminderChannels.Popup, "first run keeps popup channel");
            defaults.PreferredPath = @"C:\应用程序";
            defaults.SoundEnabled = false;
            defaults.ReminderChannel = ReminderChannels.Windows;
            defaults.IgnoredExecutables.Add(@"C:\tools\setup.exe");
            defaults.IgnoredExecutables.Add(@"c:\TOOLS\SETUP.exe");
            store.SaveSettings(defaults);
            AppSettings reload = store.LoadSettings();
            Check(reload.PreferredPath == defaults.PreferredPath && !reload.SoundEnabled, "Unicode settings roundtrip");
            Check(reload.ReminderChannel == ReminderChannels.Windows, "Windows notification preference persists");
            Check(reload.IgnoredExecutables.Count == 1, "ignore entries deduplicate case-insensitively");
            reload.NotificationsEnabled = false;
            store.SaveSettings(reload);
            Check(!store.LoadSettings().NotificationsEnabled, "atomic settings replacement");
            File.WriteAllText(store.SettingsFile, "<AppSettings><PreferredPath>C:\\LegacyApps</PreferredPath><SoundEnabled>false</SoundEnabled><NotificationsEnabled>true</NotificationsEnabled></AppSettings>");
            AppSettings legacy = store.LoadSettings();
            Check(legacy.ReminderChannel == ReminderChannels.Popup && legacy.PreferredPath == @"C:\LegacyApps" && !legacy.SoundEnabled, "v1.0 settings migrate without losing preferences");
            File.WriteAllText(store.SettingsFile, "<AppSettings><PreferredPath>C:\\LegacyApps</PreferredPath><ReminderChannel>UnknownFutureValue</ReminderChannel></AppSettings>");
            AppSettings unknown = store.LoadSettings();
            Check(unknown.ReminderChannel == ReminderChannels.Popup && unknown.PreferredPath == @"C:\LegacyApps", "unknown notification channel does not discard other settings");
            File.WriteAllText(store.SettingsFile, "<broken>");
            Check(store.LoadSettings().NotificationsEnabled, "corrupt settings recover defaults");
            Check(Directory.GetFiles(store.DirectoryPath, "settings.xml.corrupt-*").Length == 1, "corrupt settings backed up");
            for (int i = 0; i < 205; i++) store.AddHistory(new HistoryEntry { Time = DateTime.Now, Application = "app" + i, Reason = "test", Action = "提醒" });
            var history = store.LoadHistory();
            Check(history.Count == 200 && history[0].Application == "app204" && history[199].Application == "app5", "history bounded and newest first");
            store.AddHistory(new HistoryEntry { Application = "A\u0001B\ud83d\ude00", Action = "提醒" });
            Check(store.LoadHistory()[0].Application == "AB\ud83d\ude00", "invalid XML controls removed and supplementary characters preserved");
            store.AddHistory(new HistoryEntry { Application = "PathSnapshot", PreferredPath = @"C:\应用程序\原始目录", Action = "已提交系统通知" });
            Check(store.LoadHistory()[0].PreferredPath == @"C:\应用程序\原始目录", "history preserves original suggested path");
            Check(String.IsNullOrEmpty(store.LoadHistory()[1].PreferredPath), "older history without path remains readable");
            store.ClearHistory();
            Check(store.LoadHistory().Count == 0, "history clear persists");
            Check(Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "atomic writes leave no temporary files");
            string normalized, error;
            Check(!PathRules.TryValidate(@"relative\apps", out normalized, out error), "relative path rejected");
            Check(!PathRules.TryValidate(@"\\server\apps", out normalized, out error), "UNC path rejected");
            Check(!PathRules.TryValidate(@"C:\", out normalized, out error), "drive root rejected");
            Check(!PathRules.TryValidate(@"C:\Apps\..\Windows", out normalized, out error), "parent traversal rejected");
            Check(!PathRules.TryValidate(@"C:\Apps\CON.txt", out normalized, out error), "reserved device name rejected");
            Check(!PathRules.TryValidate(@"C:\Apps\bad*name", out normalized, out error), "wildcard rejected");
            Check(!PathRules.TryValidate(@"C:\Apps\folder.", out normalized, out error), "trailing dot rejected");
            Check(!PathRules.TryValidate(Environment.GetFolderPath(Environment.SpecialFolder.Windows), out normalized, out error), "Windows directory rejected");
            string proposed = Path.Combine(Path.GetPathRoot(root), "Apps-Test-" + Guid.NewGuid().ToString("N"));
            Check(PathRules.TryValidate(proposed, out normalized, out error) && normalized == proposed, "new fixed-drive folder accepted");
            Check(!Directory.Exists(proposed), "validation does not create target directory");
            string executable = Path.GetFileName(System.Windows.Forms.Application.ExecutablePath);
            Check(StartupRegistration.IsOwnedCommand("\"C:\\Moved Folder\\" + executable + "\" --background"), "startup ownership recognizes moved executable");
            Check(!StartupRegistration.IsOwnedCommand("\" --background"), "malformed startup command rejected");
            Check(!StartupRegistration.IsOwnedCommand("\"C:\\OtherProgram.exe\" --background"), "foreign startup executable rejected");
            Check(!StartupRegistration.IsOwnedCommand("\"relative\\" + executable + "\" --background"), "relative startup command rejected");
            Check(!StartupRegistration.IsOwnedCommand("\"C:\\" + executable + "\" --background --extra"), "unexpected startup arguments rejected");
            Console.WriteLine("Storage tests passed: " + count);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
