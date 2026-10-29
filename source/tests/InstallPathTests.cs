using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using InstallPathReminder;

internal static class InstallPathTests
{
    private static int count;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        count++;
        Console.WriteLine("PASS: " + name);
    }
    private static void Name(string file, string product, string description, string expected)
    {
        Check(InstallPathSuggestion.InferName(new ProcessSnapshot { Name = file, ProductName = product, FileDescription = description }) == expected, "name: " + file + " => " + expected);
    }
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiOpenDatabaseW(string path, IntPtr mode, out uint database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiDatabaseOpenViewW(uint database, string sql, out uint view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll")] private static extern uint MsiDatabaseCommit(uint database);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
    private static void Execute(uint database, string sql)
    {
        uint view;
        if (MsiDatabaseOpenViewW(database, sql, out view) != 0) throw new Exception("MSI fixture query failed");
        try { if (MsiViewExecute(view, 0) != 0) throw new Exception("MSI fixture execute failed"); }
        finally { MsiCloseHandle(view); }
    }
    private static string Hash(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return Convert.ToBase64String(sha.ComputeHash(stream));
    }
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.Combine(Path.GetPathRoot(Path.GetFullPath(args[0])), "Apps");
            Name("setup.exe", "Acme Editor", "Setup", "Acme Editor");
            Name("setup.exe", "Inno Setup", "Great App 安装程序", "Great App");
            Name("VSCodeUserSetup-x64-1.98.2.exe", "", "", "VSCode");
            Name("Firefox Setup 123.0.exe", "", "", "Firefox");
            Name("WidgetInstaller.exe", "", "", "Widget");
            Name("App-2.3.4-x64.exe", "", "", "App");
            Name("音乐播放器安装包.exe", "", "", "音乐播放器");
            Name("sample.exe", "", "Photo Editor Installer", "Photo Editor");
            Name("setup.exe", "Setup Factory", "Setup", "");
            Name("setup.exe", "", "", "");
            var one = InstallPathSuggestion.Create(root, new ProcessSnapshot { Name = "setup.exe", ProductName = "Acme" });
            Check(one.FullPath == Path.Combine(root, "Acme"), "root plus actual software folder");
            Check(!one.NeedsReview, "recognized product has reliable name source");
            var unknown = InstallPathSuggestion.Create(root, new ProcessSnapshot { Name = "setup.exe" });
            var other = InstallPathSuggestion.Create(root, new ProcessSnapshot { Name = "setup.exe" });
            Check(unknown.NeedsReview && unknown.SoftwareName.StartsWith("待确认软件-"), "unknown generic installer visibly marked for review");
            Check(unknown.FullPath != other.FullPath, "unknown installers do not share a generic Setup folder");
            var reserved = InstallPathSuggestion.Create(root, new ProcessSnapshot { Name = "setup.exe", ProductName = "CON.txt" });
            Check(Path.GetFileName(reserved.FullPath) == "_CON.txt", "reserved Windows device name escaped");
            var traversal = InstallPathSuggestion.Create(root, new ProcessSnapshot { Name = "setup.exe", ProductName = @"..\escape/evil:*?|<>" });
            Check(Path.GetDirectoryName(traversal.FullPath) == root && Path.GetFileName(traversal.FullPath).IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "untrusted product name stays within one child folder");
            var longName = InstallPathSuggestion.Create(root, new ProcessSnapshot { Name = "setup.exe", ProductName = new String('a', 79) + "\ud83d\ude00long" });
            Check(longName.SoftwareName.Length <= 80 && !Char.IsHighSurrogate(longName.SoftwareName[longName.SoftwareName.Length - 1]), "long names truncated without broken Unicode");
            Check(!Directory.Exists(other.FullPath), "suggestion does not create installation folders");
            Check(InstallPathSuggestion.GetMsiPackagePath("msiexec /i \"C:\\Downloads\\Cool App 2.0.msi\" /qn") == @"C:\Downloads\Cool App 2.0.msi", "quoted MSI path extracted");
            Check(InstallPathSuggestion.InferName(new ProcessSnapshot { Name = "msiexec.exe", CommandLine = "msiexec /i \"C:\\Missing\\Cool App 2.0.msi\"" }) == "Cool App", "MSI file name used when package metadata unavailable");
            Check(InstallPathSuggestion.InferName(new ProcessSnapshot { Name = "msiexec.exe", CommandLine = "msiexec /i {12345678-1234-1234-1234-123456789abc}" }) == "", "MSI product code is not invented as software name");
            string directory = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string package = Path.Combine(directory, "setup.msi");
            uint db;
            if (MsiOpenDatabaseW(package, new IntPtr(3), out db) != 0) throw new Exception("MSI fixture create failed");
            try
            {
                Execute(db, "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)");
                Execute(db, "INSERT INTO `Property` (`Property`, `Value`) VALUES ('ProductName', '示范编辑器')");
                if (MsiDatabaseCommit(db) != 0) throw new Exception("MSI fixture commit failed");
            }
            finally { MsiCloseHandle(db); }
            string before = Hash(package);
            string msiName = InstallPathSuggestion.InferName(new ProcessSnapshot { Name = "msiexec.exe", CommandLine = "msiexec /i \"" + package + "\"" });
            Check(msiName == "示范编辑器", "actual MSI ProductName read from local database");
            Check(Hash(package) == before, "MSI metadata read leaves package unchanged");
            Console.WriteLine("Install path tests passed: " + count);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
