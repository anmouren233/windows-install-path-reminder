using System;
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
            Build(entry, test).Show();
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
