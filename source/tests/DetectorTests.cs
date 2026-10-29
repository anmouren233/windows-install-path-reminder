using System;
using InstallPathReminder;

internal static class DetectorTests
{
    private static int tests;
    private static void Check(string name, string command, string description, bool expected)
    {
        bool actual = InstallerClassifier.Classify(new ProcessSnapshot { Name = name, CommandLine = command, FileDescription = description }).IsInstaller;
        tests++;
        if (actual != expected) throw new Exception("FAIL " + name + " | " + command + " | " + description + ": expected " + expected + ", got " + actual);
    }
    public static int Main()
    {
        try
        {
            Check("setup.exe", "", "", true);
            Check("ChromeSetup.exe", "", "", true);
            Check("VSCodeUserSetup-x64-1.94.0.exe", "", "", true);
            Check("myapp_installer_2026.exe", "", "", true);
            Check("install.exe", "", "", true);
            Check("软件安装程序.exe", "", "", true);
            Check("python-3.13-amd64.exe", "", "Python 3.13 Installer", true);
            Check("myapp.exe", "", "My App Setup", true);
            Check("myapp.exe", "", "软件安装向导", true);
            Check("explorer.exe", "C:\\setup\\explorer.exe", "Windows Explorer", false);
            Check("notepad.exe", "notepad C:\\installer\\notes.txt", "Notepad", false);
            Check("installedApp.exe", "", "Application installed in C:\\Apps", false);
            Check("ordinary.exe", "", "Application located in C:\\Setup\\Apps", false);
            Check("ordinary.exe", "", "Located at \\\\server\\installer\\App.exe", false);
            Check("setupservice.exe", "", "Background service", false);
            Check("unins000.exe", "", "Setup", false);
            Check("Uninstall.exe", "", "Installer", false);
            Check("AppUpdater.exe", "", "Installer", false);
            Check("软件卸载.exe", "", "安装程序", false);
            Check("安装更新程序.exe", "", "安装程序", false);
            Check("setup.exe", "", "Software Updater", false);
            Check("setup.exe", "setup.exe /uninstall", "", false);
            Check("setup.exe", "setup.exe --uninstall", "", false);
            Check("setup.exe", "setup.exe /repair", "", false);
            Check("setup.exe", "setup.exe --repair", "", false);
            Check("setup.exe", "setup.exe /update", "", false);
            Check("setup.exe", "setup.exe --update", "", false);
            Check("setup.exe", "\"C:\\My App\\setup.exe\" /UNINSTALL /quiet", "", false);
            Check("setup.exe", "\"C:\\My App\\setup.exe\" /install", "", true);
            Check("setup.exe", "setup.exe --install --update-path C:\\Data", "", true);
            Check("setup.exe", "setup.exe /install /log \"C:\\Data /update logs\\output.txt\"", "", true);
            Check("setup.exe", "\"C:\\Some /repair folder\\setup.exe\" /install", "", true);
            Check("setup.exe", "setup.exe /install --message \"please --uninstall the previous version\"", "", true);
            Check("setup.exe", "setup.exe /install --message \"/uninstall\"", "", true);
            Check("setup.exe", "setup.exe /install --message \"word \\\" /update \\\" word\"", "", true);
            Check("setup.exe", "\"C:\\Some folder\\setup.exe\" /log \"C:\\Logs\\\" /repair", "", true);
            Check("setup.exe", "\"C:\\Some folder\\setup.exe\" /log \"C:\\Logs\\\\\" /repair", "", false);
            Check("setup.exe", "setup.exe\t--repair\t/quiet", "", false);
            Check("InstallUtil.exe", "", "", false);
            Check("TrustedInstaller.exe", "", "", false);
            Check("SetupHost.exe", "", "Setup", false);
            Check("msiexec.exe", "msiexec /i C:\\app.msi", "", true);
            Check("msiexec.exe", "msiexec /I\"C:\\My App\\app.msi\" /quiet", "", true);
            Check("msiexec.exe", "msiexec /iC:\\app.msi", "", true);
            Check("msiexec.exe", "msiexec /package \"C:\\app.msi\"", "", true);
            Check("msiexec.exe", "msiexec /a C:\\app.msi", "", true);
            Check("msiexec.exe", "msiexec /i {1234-5678}", "", true);
            Check("msiexec.exe", "msiexec /x C:\\app.msi", "", false);
            Check("msiexec.exe", "msiexec /uninstall {1234-5678}", "", false);
            Check("msiexec.exe", "msiexec /i C:\\app.msi /update patch.msp", "", false);
            Check("msiexec.exe", "msiexec /p patch.msp", "", false);
            Check("msiexec.exe", "msiexec /fvomus C:\\app.msi", "", false);
            Check("msiexec.exe", "msiexec /V", "", false);
            Check("msiexec.exe", "msiexec -Embedding ABC", "", false);
            Check("msiexec.exe", "", "Windows Installer", false);
            Check("msiexec.exe", "msiexec /help", "", false);
            Check("msiexec.exe", "msiexec /invalid", "", false);
            Check(null, null, null, false);
            if (InstallerClassifier.Classify(null).IsInstaller) throw new Exception("FAIL null snapshot");
            Console.WriteLine("PASS: " + (tests + 1) + " installer classification cases.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
