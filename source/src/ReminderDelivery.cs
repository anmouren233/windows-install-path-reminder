using System;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace InstallPathReminder
{
    public static class ReminderChannels
    {
        public const string Popup = "Popup";
        public const string Windows = "Windows";

        public static string Normalize(string value)
        {
            return String.Equals(value, Windows, StringComparison.OrdinalIgnoreCase) ? Windows : Popup;
        }

        public static string DisplayName(string value)
        {
            return Normalize(value) == Windows ? "Windows 原生通知" : "弹窗提醒";
        }
    }

    internal sealed class WindowsNotificationDelivery : IDisposable
    {
        private readonly OnActivated activated;
        private bool initialized;
        internal event Action<string, string> Failed;
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

        internal static string ShellStatus()
        {
            int state;
            try { if (SHQueryUserNotificationState(out state) != 0) return "无法读取系统显示状态"; }
            catch { return "无法读取系统显示状态"; }
            return DescribeShellState(state);
        }

        internal static string DescribeShellState(int state)
        {
            switch (state)
            {
                case 2: case 3: case 4: return "系统处于忙碌、全屏或演示状态，可能不显示横幅";
                case 1: return "当前会话非活动状态，可能不显示横幅";
                case 6: return "系统处于安静时段，可能不显示横幅";
                case 5: return "系统允许显示；横幅仍受勿扰模式及应用通知设置控制";
                default: return "请检查 Windows 通知中心、勿扰模式及应用横幅设置";
            }
        }

        internal WindowsNotificationDelivery(Action<string> action)
        {
            activated = delegate(ToastNotificationActivatedEventArgsCompat args) { action(args.Argument); };
        }

        internal void Initialize()
        {
            if (initialized) return;
            ToastNotificationManagerCompat.OnActivated += activated;
            initialized = true;
        }

        internal void Show(HistoryEntry entry, bool test)
        {
            Initialize();
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            var setting = notifier.Setting;
            if (setting != Windows.UI.Notifications.NotificationSetting.Enabled)
                throw new InvalidOperationException("Windows 已阻止通知（" + setting + "）。请在系统设置 → 通知中检查应用和全局开关；组策略限制请联系管理员。");
            Build(entry, test).Show(delegate(Windows.UI.Notifications.ToastNotification toast)
            {
                toast.Failed += delegate(Windows.UI.Notifications.ToastNotification sender, Windows.UI.Notifications.ToastFailedEventArgs e)
                {
                    var callback = Failed;
                    if (callback != null) callback(entry.NotificationId, "Windows 异步发送失败：" + e.ErrorCode.Message + " (0x" + e.ErrorCode.HResult.ToString("X8") + ")");
                };
            });
        }

        internal static ToastContentBuilder Build(HistoryEntry entry, bool test)
        {
            Guid id;
            if (entry == null || !Guid.TryParseExact(entry.NotificationId, "N", out id) || String.IsNullOrWhiteSpace(entry.SuggestedPath))
                throw new ArgumentException("通知缺少有效的记录或安装路径。");
            return new ToastContentBuilder()
                .AddArgument("action", "details").AddArgument("id", entry.NotificationId)
                .AddText(test ? "安装路径提醒 · 测试" : "安装前，请检查安装位置")
                .AddText(entry.SoftwareName + "\n建议安装路径：" + entry.SuggestedPath)
                .AddText("点击下方按钮复制完整路径，再粘贴到安装向导。")
                .AddButton(new ToastButton().SetContent("一键复制建议安装路径")
                    .AddArgument("action", "copy").AddArgument("id", entry.NotificationId));
        }

        internal static bool TryParseAction(string argument, out string action, out string id)
        {
            action = id = null;
            if (String.IsNullOrWhiteSpace(argument) || argument.Length > 256) return false;
            try
            {
                var args = ToastArguments.Parse(argument);
                Guid parsed;
                return args.TryGetValue("action", out action) && (action == "copy" || action == "details") &&
                    args.TryGetValue("id", out id) && Guid.TryParseExact(id, "N", out parsed);
            }
            catch (ArgumentException) { return false; }
            catch (FormatException) { return false; }
        }

        internal static void Uninstall() { ToastNotificationManagerCompat.Uninstall(); }

        public void Dispose()
        {
            if (initialized) ToastNotificationManagerCompat.OnActivated -= activated;
        }
    }
}
