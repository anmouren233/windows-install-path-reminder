using System;
using System.Xml;
using InstallPathReminder;

internal static class NotificationTests
{
    private static int count;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        count++;
        Console.WriteLine("PASS: " + name);
    }
    private static int Main()
    {
        try
        {
            Check(ReminderChannels.Normalize(null) == ReminderChannels.Popup, "legacy channel defaults to popup");
            Check(ReminderChannels.Normalize("windows") == ReminderChannels.Windows, "Windows preference normalized");
            HistoryEntry entry = new HistoryEntry { NotificationId = Guid.NewGuid().ToString("N"), SoftwareName = "A & B 软件", SuggestedPath = @"C:\Apps\A & B 软件" };
            XmlDocument xml = new XmlDocument();
            xml.LoadXml(WindowsNotificationDelivery.Build(entry, false).GetToastContent().GetContent());
            XmlNode button = xml.SelectSingleNode("/toast/actions/action");
            Check(button != null && button.Attributes["content"].Value == "一键复制建议安装路径", "native toast contains explicit one-click copy button");
            Check(xml.InnerText.Contains(entry.SuggestedPath), "toast shows full suggested installation path");
            string action, id;
            Check(WindowsNotificationDelivery.TryParseAction(button.Attributes["arguments"].Value, out action, out id) && action == "copy" && id == entry.NotificationId, "copy button carries exact record ID");
            Check(WindowsNotificationDelivery.TryParseAction(xml.DocumentElement.Attributes["launch"].Value, out action, out id) && action == "details" && id == entry.NotificationId, "toast body points to matching detail");
            Check(!button.Attributes["arguments"].Value.Contains(entry.SuggestedPath), "activation payload contains no clipboard text");
            Check(!WindowsNotificationDelivery.TryParseAction("action=copy;id=not-a-guid", out action, out id), "invalid notification IDs rejected");
            Check(!WindowsNotificationDelivery.TryParseAction("action=execute;id=" + entry.NotificationId, out action, out id), "unrecognized actions rejected");
            Check(!WindowsNotificationDelivery.TryParseAction(new String('x', 257), out action, out id), "oversized arguments rejected");
            entry.NotificationId = null;
            bool rejected = false;
            try { WindowsNotificationDelivery.Build(entry, true); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "toast cannot be sent without persistent record identity");
            Console.WriteLine("Notification tests passed: " + count);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
