using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using InstallPathReminder;

internal static class DeliveryIntegrationTests
{
    private static int count;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        count++;
        Console.WriteLine("PASS: " + name);
    }
    private static object Field(object instance, string name)
    {
        return instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
    }
    private static int PopupCount { get { return Application.OpenForms.Cast<Form>().Count(f => f is ReminderWindow); } }
    private static TextBox FindPath(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            TextBox text = child as TextBox;
            if (text != null && text.AccessibleName == "建议安装路径") return text;
            TextBox descendant = FindPath(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            int delivered = 0;
            string deliveredPath = null;
            string clipboard = "unchanged";
            bool clipboardBusy = false;
            bool fail = false;
            Action<HistoryEntry, bool> native = delegate(HistoryEntry entry, bool test)
            {
                if (fail) throw new InvalidOperationException("Simulated notification failure");
                delivered++;
                deliveredPath = entry.SuggestedPath;
            };
            Action<string> copy = delegate(string value) { if (clipboardBusy) throw new System.Runtime.InteropServices.ExternalException(); clipboard = value; };
            string dataDirectory = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
            string persistedId = null;
            using (ReminderContext context = new ReminderContext(dataDirectory, true, native, false, copy))
            {
                context.TestReminder(ReminderChannels.Windows, @"C:\OriginalApps", false);
                Check(delivered == 1 && deliveredPath == @"C:\OriginalApps\示例软件", "native preview appends software folder");
                Check(PopupCount == 0, "native mode does not also open a popup");
                Check(context.Settings.ReminderChannel == ReminderChannels.Popup, "preview does not change saved channel");
                var original = context.Store.LoadHistory()[0];
                persistedId = original.NotificationId;
                Check(original.Action.Contains("已提交系统") && original.SuggestedPath == @"C:\OriginalApps\示例软件", "native submission records full suggested path");

                context.Settings.ReminderChannel = ReminderChannels.Windows;
                context.Settings.PreferredPath = @"C:\ChangedApps";
                context.SaveSettings(context.Settings);
                context.TestReminder();
                Check(delivered == 2 && deliveredPath == @"C:\ChangedApps\示例软件", "default dispatch uses saved native channel");
                Check(context.Store.LoadSettings().ReminderChannel == ReminderChannels.Windows, "saved native preference survives storage reload");

                string error;
                Check(context.TryHandleNotificationAction("action=copy;id=" + original.NotificationId, out error) && clipboard == @"C:\OriginalApps\示例软件", "old toast copies its own path after root changes");
                var latest = context.Store.LoadHistory()[0];
                Check(context.TryHandleNotificationAction("action=copy;id=" + latest.NotificationId, out error) && clipboard == @"C:\ChangedApps\示例软件", "new toast copies its own distinct path");
                Check(!context.TryHandleNotificationAction("action=copy;id=" + Guid.NewGuid().ToString("N"), out error) && clipboard == @"C:\ChangedApps\示例软件", "expired toast does not copy latest path");
                Check(!context.TryHandleNotificationAction("action=copy;path=C:\\Untrusted", out error) && clipboard == @"C:\ChangedApps\示例软件", "callback cannot inject an arbitrary clipboard path");
                clipboardBusy = true;
                Check(!context.TryHandleNotificationAction("action=copy;id=" + original.NotificationId, out error) && error.Contains("剪贴板"), "busy clipboard reports failure");
                clipboardBusy = false;

                Check(context.TryHandleNotificationAction("action=details;id=" + original.NotificationId, out error), "toast body opens matching detail");
                ReminderWindow popup = Application.OpenForms.Cast<Form>().OfType<ReminderWindow>().Single();
                Check(FindPath(popup).Text == @"C:\OriginalApps\示例软件", "historical detail preserves full path after settings change");
                Check(context.TryCopySuggestedPath(@"C:\CustomApps\CustomFolder", out error) && clipboard == @"C:\CustomApps\CustomFolder", "editable popup path can be copied");
                Check(!context.TryCopySuggestedPath(@"C:\Apps\..\Windows", out error) && clipboard == @"C:\CustomApps\CustomFolder", "invalid edited path does not replace clipboard");
                int records = context.Store.LoadHistory().Count;
                popup.Close();
                Check(context.Store.LoadHistory().Count == records, "viewing history does not generate another alert record");

                context.TestReminder(ReminderChannels.Popup, @"C:\PopupApps", false);
                Check(PopupCount == 1 && delivered == 2, "popup preview does not submit a native notification");
                context.TestReminder(ReminderChannels.Windows, @"C:\ConcurrentApps", false);
                Check(PopupCount == 1 && delivered == 3, "native notification can be sent while a detail popup is open");
                Application.OpenForms.Cast<Form>().OfType<ReminderWindow>().Single().Close();

                fail = true;
                context.TestReminder(ReminderChannels.Windows, @"C:\FailedApps", false);
                Check(PopupCount == 0 && context.Store.LoadHistory()[0].Action.Contains("失败"), "native delivery failure is recorded without switching to popup");
                Check(context.Store.LoadHistory()[0].NotificationDetails.Contains("Simulated notification failure"), "synchronous failure detail survives storage reload");
                context.ReportNativeFailure(original.NotificationId, "Windows failure 0x80004005");
                Check(context.Store.FindHistory(original.NotificationId).NotificationDetails == "Windows failure 0x80004005" && context.Store.FindHistory(latest.NotificationId).Action.Contains("已提交"), "asynchronous failure updates only its matching notification");
                context.Quit();
            }
            using (ReminderContext reopened = new ReminderContext(dataDirectory, true, native, false, copy))
            {
                string error;
                Check(reopened.TryHandleNotificationAction("action=copy;id=" + persistedId, out error) && clipboard == @"C:\OriginalApps\示例软件", "restart reloads the original toast path from persistent history");
                reopened.Quit();
            }
            Console.WriteLine("Delivery integration tests passed: " + count);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
