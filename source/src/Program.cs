using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InstallPathReminder
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread]
        internal static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool toastActivated = Array.Exists(Environment.GetCommandLineArgs(), delegate(string arg) { return arg == "-ToastActivated"; });
            if (Array.Exists(Environment.GetCommandLineArgs(), delegate(string arg) { return arg == "--unregister-notifications"; }))
            {
                try { WindowsNotificationDelivery.Uninstall(); }
                catch (Exception ex) { MessageBox.Show("清除通知注册失败：" + ex.Message, "安装路径提醒"); }
                return;
            }
            string data = null;
            bool background = toastActivated;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--background") background = true;
                else if (args[i] == "--data-dir" && i + 1 < args.Length)
                {
                    try { data = Path.GetFullPath(args[++i]); }
                    catch (Exception ex) { MessageBox.Show("数据目录无效：" + ex.Message, "安装路径提醒"); return; }
                }
            }
            bool created;
            using (Mutex single = new Mutex(true, "Local\\InstallPathReminder-" + Process.GetCurrentProcess().SessionId, out created))
            {
                if (!created)
                {
                    if (toastActivated) return;
                    MessageBox.Show("安装路径提醒已在运行。\n请双击右下角的托盘图标打开设置。", "安装路径提醒", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { using (ReminderContext context = new ReminderContext(data, background, null, true, null, toastActivated)) Application.Run(context); }
                catch (Exception ex) { MessageBox.Show("程序无法继续运行：\n" + ex.Message, "安装路径提醒", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { single.ReleaseMutex(); }
            }
        }
    }

    internal sealed class ReminderContext : ApplicationContext
    {
        internal readonly LocalStore Store;
        internal AppSettings Settings;
        internal readonly bool TestDataDirectory;
        internal MainWindow Window;
        private readonly NotifyIcon tray;
        private readonly Action<HistoryEntry, bool> submitWindowsNotification;
        private readonly WindowsNotificationDelivery windowsNotifications;
        private readonly Action<string> writeClipboard;
        private readonly ProcessMonitor monitor;
        private readonly ToolStripMenuItem pauseItem;
        private readonly Queue<PendingReminder> pending = new Queue<PendingReminder>();
        private ReminderWindow activeReminder;
        private bool closing;
        internal string MonitorStatus = "正在启动监测…";
        internal bool MonitorReady;

        internal ReminderContext(string data, bool background, Action<HistoryEntry, bool> nativeDelivery = null, bool startMonitor = true, Action<string> clipboardWriter = null, bool toastActivated = false)
        {
            TestDataDirectory = data != null;
            Store = new LocalStore(data);
            Settings = Store.LoadSettings();
            writeClipboard = clipboardWriter ?? ClipboardPaths.Write;
            if (!TestDataDirectory) Settings.StartWithWindows = StartupRegistration.IsEnabled();
            Window = new MainWindow(this);
            IntPtr handle = Window.Handle;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开设置", null, delegate { ShowMain(); });
            menu.Items.Add("查看提醒记录", null, delegate { ShowHistory(); });
            menu.Items.Add("测试提醒", null, delegate { TestReminder(); });
            pauseItem = new ToolStripMenuItem("暂停提醒");
            pauseItem.Click += delegate { SetEnabled(!Settings.NotificationsEnabled); };
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("清除原生通知并退出", null, delegate
            {
                try { WindowsNotificationDelivery.Uninstall(); Quit(); }
                catch (Exception ex) { Visuals.Error(Window, "清除通知注册失败：" + ex.Message); }
            });
            menu.Items.Add("退出", null, delegate { Quit(); });
            tray = new NotifyIcon { Icon = Visuals.AppIcon, Text = "安装路径提醒", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { ShowMain(); };
            if (nativeDelivery == null)
            {
                windowsNotifications = new WindowsNotificationDelivery(delegate(string arguments)
                {
                    Post(delegate
                    {
                        string error;
                        if (!TryHandleNotificationAction(arguments, out error)) Visuals.Error(Window, error);
                    });
                });
                submitWindowsNotification = windowsNotifications.Show;
                windowsNotifications.Failed += delegate(string id, string error)
                {
                    Post(delegate { ReportNativeFailure(id, error); });
                };
                // Register activation at every startup so older notifications still work
                // after a normal restart or a switch back to popup reminders.
                try { windowsNotifications.Initialize(); }
                catch (Exception ex) { MonitorStatus = "通知操作初始化失败：" + ex.Message; }
            }
            else submitWindowsNotification = nativeDelivery;
            monitor = new ProcessMonitor();
            monitor.InstallerDetected += delegate(ProcessSnapshot process, string reason) { Post(delegate { Enqueue(process, reason, false); }); };
            monitor.StatusChanged += delegate(string status) { Post(delegate { MonitorStatus = status; Window.RefreshStatus(); }); };
            monitor.Paused = !Settings.NotificationsEnabled;
            UpdateTray();
            if (!background) Window.Show();
            if (startMonitor) ThreadPool.QueueUserWorkItem(delegate
            {
                try { monitor.Start(); Post(delegate { MonitorReady = true; MonitorStatus = monitor.Mode; Window.RefreshStatus(); }); }
                catch (Exception ex) { Post(delegate { MonitorStatus = "监测启动失败：" + ex.Message; Window.RefreshStatus(); }); }
            });
        }

        private void Post(MethodInvoker action)
        {
            if (closing || Window.IsDisposed) return;
            try { Window.BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        internal void ShowMain()
        {
            Window.Show();
            Window.WindowState = FormWindowState.Normal;
            Window.Activate();
            Window.RefreshLists();
        }

        internal void ShowHistory()
        {
            if (closing) return;
            ShowMain();
            Window.ShowHistory();
        }

        internal void ShowHistoryDetail(HistoryEntry entry)
        {
            if (closing || entry == null) return;
            string suggested = entry.SuggestedPath;
            string softwareName = entry.SoftwareName;
            if (String.IsNullOrWhiteSpace(suggested))
            {
                try
                {
                    var generated = InstallPathSuggestion.Create(String.IsNullOrWhiteSpace(entry.PreferredPath) ? Settings.PreferredPath : entry.PreferredPath,
                        new ProcessSnapshot { Name = entry.Application, ExecutablePath = entry.ExecutablePath });
                    suggested = generated.FullPath;
                    softwareName = generated.SoftwareName;
                }
                catch (Exception ex) { Visuals.Error(Window, "旧记录无法生成软件目录：" + ex.Message); return; }
            }
            OpenPopup(new PendingReminder {
                Process = new ProcessSnapshot { Name = entry.Application, ExecutablePath = entry.ExecutablePath },
                Reason = entry.Time.ToString("yyyy-MM-dd HH:mm:ss") + " · " + entry.Reason,
                PreferredPath = suggested, SoftwareName = softwareName,
                Test = (entry.Action ?? "").Contains("测试"), FromHistory = true
            }, false);
        }

        internal bool TryHandleNotificationAction(string arguments, out string error)
        {
            error = "";
            string action, id;
            if (!WindowsNotificationDelivery.TryParseAction(arguments, out action, out id)) { error = "这条通知的操作信息无效。"; return false; }
            try
            {
                HistoryEntry entry = Store.FindHistory(id);
                if (entry == null || String.IsNullOrWhiteSpace(entry.SuggestedPath)) { error = "这条提醒记录已过期，无法复制。请重新触发安装提醒。"; return false; }
                if (action == "copy")
                {
                    if (!TryCopySuggestedPath(entry.SuggestedPath, out error)) return false;
                    Window.ShowCopyStatus(entry.SuggestedPath);
                }
                else ShowHistoryDetail(entry);
                return true;
            }
            catch (Exception ex) { error = "无法处理通知操作：" + ex.Message; return false; }
        }

        internal bool TryCopySuggestedPath(string path, out string error)
        {
            string normalized;
            if (!PathRules.TryValidate(path, out normalized, out error)) return false;
            try { writeClipboard(normalized); return true; }
            catch (ExternalException) { error = "剪贴板暂时被占用，请稍后再点击复制。"; return false; }
            catch (Exception ex) { error = "复制失败：" + ex.Message; return false; }
        }

        internal void SaveSettings(AppSettings settings, bool updateStartup = true)
        {
            bool startupChanged = updateStartup && !TestDataDirectory && (settings.StartWithWindows ? !StartupRegistration.IsEnabled() : StartupRegistration.HasOwnedEntry());
            string previousStartup = startupChanged ? StartupRegistration.GetOwnedCommand() : null;
            if (startupChanged) StartupRegistration.SetEnabled(settings.StartWithWindows);
            try { Store.SaveSettings(settings); }
            catch
            {
                if (startupChanged) { try { StartupRegistration.RestoreOwnedCommand(previousStartup); } catch { } }
                throw;
            }
            Settings = settings;
            monitor.Paused = !settings.NotificationsEnabled;
            if (!settings.NotificationsEnabled) pending.Clear();
            UpdateTray();
            Window.RefreshStatus();
        }

        private void UpdateTray()
        {
            pauseItem.Checked = !Settings.NotificationsEnabled;
            tray.Text = Settings.NotificationsEnabled ? "安装路径提醒 · 正在监测" : "安装路径提醒 · 已暂停";
        }

        internal void SetEnabled(bool enabled)
        {
            bool previous = Settings.NotificationsEnabled;
            Settings.NotificationsEnabled = enabled;
            try { SaveSettings(Settings, false); Window.LoadSettings(); }
            catch (Exception ex) { Settings.NotificationsEnabled = previous; Visuals.Error(Window, ex.Message); }
        }

        internal void TestReminder(string channel = null, string preferredPath = null, bool? sound = null)
        {
            Enqueue(new ProcessSnapshot { Name = "示例软件 Setup.exe", ExecutablePath = "", FileDescription = "示例软件安装程序" }, "这是测试提醒，没有运行真实安装程序。", true, channel, preferredPath, sound);
        }

        internal void ReportNativeFailure(string id, string error)
        {
            try { Store.UpdateHistoryAction(id, "系统通知发送失败", error); }
            catch (Exception ex) { error += "；无法保存记录：" + ex.Message; }
            Window.ShowNotificationStatus(error, true);
            Window.RefreshLists();
        }

        private void Enqueue(ProcessSnapshot process, string reason, bool test, string channel = null, string preferredPath = null, bool? sound = null)
        {
            if (closing || (!test && !Settings.NotificationsEnabled)) return;
            if (!test && !String.IsNullOrEmpty(process.ExecutablePath) && Settings.IgnoredExecutables.Exists(delegate(string path) { return String.Equals(path, process.ExecutablePath, StringComparison.OrdinalIgnoreCase); })) return;
            if (pending.Count >= 20) return;
            string root = preferredPath ?? Settings.PreferredPath;
            InstallPathSuggestion suggestion;
            try { suggestion = InstallPathSuggestion.Create(root, process); }
            catch (Exception ex) { MonitorStatus = "无法生成建议路径：" + ex.Message; Window.RefreshStatus(); return; }
            pending.Enqueue(new PendingReminder {
                Process = process, Reason = reason, Test = test,
                Channel = ReminderChannels.Normalize(channel ?? Settings.ReminderChannel),
                RootPath = root, PreferredPath = suggestion.FullPath, SoftwareName = suggestion.SoftwareName, NeedsReview = suggestion.NeedsReview,
                SoundEnabled = sound ?? Settings.SoundEnabled
            });
            ShowNext();
        }

        private void ShowNext()
        {
            if (closing || pending.Count == 0) return;
            PendingReminder item = pending.Peek();
            while (!item.Test && (!Settings.NotificationsEnabled || Settings.IgnoredExecutables.Exists(delegate(string path) { return String.Equals(path, item.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase); })))
            {
                pending.Dequeue();
                if (pending.Count == 0) return;
                item = pending.Peek();
            }
            if (activeReminder != null && item.Channel == ReminderChannels.Popup) return;
            pending.Dequeue();
            HistoryEntry entry = new HistoryEntry {
                NotificationId = Guid.NewGuid().ToString("N"), Time = DateTime.Now,
                Application = item.Process.Name, ExecutablePath = item.Process.ExecutablePath, Reason = item.Reason,
                Action = "待发送", PreferredPath = item.RootPath, SuggestedPath = item.PreferredPath, SoftwareName = item.SoftwareName
            };
            // Persist the exact path before a toast can be clicked, including after app restart.
            try { Store.AddHistory(entry); }
            catch (Exception ex) { MonitorStatus = "记录保存失败，未发送提醒：" + ex.Message; Window.RefreshStatus(); Post(delegate { ShowNext(); }); return; }
            string action;
            string notificationDetails = null;
            if (item.Channel == ReminderChannels.Windows)
            {
                try
                {
                    submitWindowsNotification(entry, item.Test);
                    action = item.Test ? "测试 · 已提交系统" : "已提交系统通知";
                    notificationDetails = "已提交系统；" + WindowsNotificationDelivery.ShellStatus() + "。";
                    if (item.Test) Window.ShowNotificationStatus(notificationDetails, false);
                }
                catch (Exception ex)
                {
                    // Keep the selected channel. Do not silently create an intrusive popup.
                    action = item.Test ? "测试 · 系统通知失败" : "系统通知发送失败";
                    MonitorStatus = "通知发送失败，请查看提醒记录：" + ex.Message;
                    notificationDetails = ex.ToString();
                    Window.ShowNotificationStatus("通知发送失败：" + ex.Message, true);
                    Window.RefreshStatus();
                }
            }
            else
            {
                OpenPopup(item, item.SoundEnabled);
                action = item.Test ? "测试 · 弹窗" : "已显示弹窗";
            }
            try { Store.UpdateHistoryAction(entry.NotificationId, action, notificationDetails); }
            catch (Exception ex) { MonitorStatus = "记录保存失败：" + ex.Message; Window.RefreshStatus(); }
            Window.RefreshLists();
            if (item.Channel == ReminderChannels.Windows) Post(delegate { ShowNext(); });
        }

        private void OpenPopup(PendingReminder item, bool playSound)
        {
            if (activeReminder != null) activeReminder.Close();
            ReminderWindow popup = new ReminderWindow(this, item);
            activeReminder = popup;
            popup.FormClosed += delegate
            {
                if (Object.ReferenceEquals(activeReminder, popup)) activeReminder = null;
                if (!closing) Post(delegate { ShowNext(); });
            };
            popup.Show();
            popup.Activate();
            if (playSound) { try { SystemSounds.Asterisk.Play(); } catch { } }
        }

        internal void Ignore(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            if (!Settings.IgnoredExecutables.Exists(delegate(string existing) { return String.Equals(existing, path, StringComparison.OrdinalIgnoreCase); }))
            {
                Settings.IgnoredExecutables.Add(path);
                try { Store.SaveSettings(Settings); }
                catch { Settings.IgnoredExecutables.Remove(path); throw; }
            }
            Window.RefreshLists();
        }

        internal void Quit()
        {
            if (closing) return;
            closing = true;
            pending.Clear();
            monitor.Dispose();
            if (windowsNotifications != null) windowsNotifications.Dispose();
            tray.Visible = false;
            tray.Dispose();
            if (activeReminder != null) activeReminder.Close();
            Window.AllowClose = true;
            Window.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !closing) Quit();
            base.Dispose(disposing);
        }
    }

    internal sealed class PendingReminder
    {
        internal ProcessSnapshot Process;
        internal string Reason;
        internal bool Test;
        internal string Channel;
        internal string PreferredPath;
        internal string RootPath;
        internal string SoftwareName;
        internal bool NeedsReview;
        internal bool SoundEnabled;
        internal bool FromHistory;
    }

    internal static class ClipboardPaths
    {
        internal static void Write(string text)
        {
            DataObject data = new DataObject();
            data.SetData(DataFormats.UnicodeText, false, text);
            Clipboard.SetDataObject(data, true, 5, 100);
        }
    }

    internal static class Visuals
    {
        internal static readonly Color Ink = Color.FromArgb(28, 46, 43);
        internal static readonly Color Muted = Color.FromArgb(91, 109, 105);
        internal static readonly Color Green = Color.FromArgb(25, 112, 90);
        internal static readonly Color Background = Color.FromArgb(246, 248, 247);
        internal static readonly Icon AppIcon = CreateIcon();
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
        private static Icon CreateIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush brush = new SolidBrush(Green)) g.FillEllipse(brush, 1, 1, 30, 30);
                using (Pen pen = new Pen(Color.White, 2.5f)) { g.DrawLines(pen, new[] { new Point(8, 17), new Point(13, 22), new Point(24, 11) }); }
                IntPtr h = bitmap.GetHicon();
                Icon result = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return result;
            }
        }
        internal static Label Label(string text, int size, bool bold)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Ink, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 8) };
        }
        internal static Button Button(string text, bool primary)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(112, 38), Padding = new Padding(10, 4, 10, 4), FlatStyle = FlatStyle.Flat, BackColor = primary ? Green : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            button.FlatAppearance.BorderColor = primary ? Green : Color.FromArgb(205, 215, 211);
            return button;
        }
        internal static void Error(IWin32Window owner, string message) { MessageBox.Show(owner, message, "安装路径提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        internal static void Copy(IWin32Window owner, string path)
        {
            try { Clipboard.SetText(path); }
            catch (ExternalException) { Error(owner, "剪贴板暂时被占用，请稍后再试。建议目录：\n" + path); }
        }
    }
}
