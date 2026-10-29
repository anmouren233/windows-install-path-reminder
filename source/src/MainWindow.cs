using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace InstallPathReminder
{
    internal sealed class MainWindow : Form
    {
        private readonly ReminderContext context;
        private readonly Label status;
        private readonly Label detail;
        private readonly TextBox preferred;
        private readonly CheckBox enabled;
        private readonly CheckBox sound;
        private readonly CheckBox startup;
        private readonly Label saved;
        private readonly ListView history;
        private readonly ListBox ignored;
        private readonly ComboBox channel;
        private readonly Label channelHint;
        private readonly TabControl tabs;
        private readonly TabPage historyTab;
        internal bool AllowClose;

        internal MainWindow(ReminderContext context)
        {
            SuspendLayout();
            this.context = context;
            Text = "安装路径提醒";
            Icon = Visuals.AppIcon;
            Font = new Font("Microsoft YaHei UI", 10);
            ForeColor = Visuals.Ink;
            BackColor = Visuals.Background;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(860, 660);
            MinimumSize = new Size(820, 680);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(30, 24, 30, 20), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Controls.Add(root);

            FlowLayoutPanel heading = Vertical();
            heading.Controls.Add(Visuals.Label("给软件，选个合适的位置。", 22, true));
            Label subtitle = Visuals.Label("安装程序启动时提醒你检查路径，让软件按你的习惯存放。", 10, false);
            subtitle.ForeColor = Visuals.Muted;
            heading.Controls.Add(subtitle);
            root.Controls.Add(heading, 0, 0);

            Panel card = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(230, 242, 236), Padding = new Padding(14, 10, 14, 8), Margin = new Padding(0, 0, 0, 12) };
            status = Visuals.Label("● 正在监测安装程序", 11, true);
            status.Dock = DockStyle.Top;
            detail = Visuals.Label("正在启动监测…", 9, false);
            detail.ForeColor = Visuals.Muted;
            detail.Dock = DockStyle.Bottom;
            card.Controls.Add(status);
            card.Controls.Add(detail);
            root.Controls.Add(card, 0, 1);

            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 8), Margin = new Padding(0, 0, 0, 14) };
            TabPage settingsTab = new TabPage("常用设置") { BackColor = Color.White, Padding = new Padding(20, 18, 20, 12) };
            historyTab = new TabPage("提醒记录") { BackColor = Color.White, Padding = new Padding(12) };
            TabPage ignoredTab = new TabPage("忽略列表") { BackColor = Color.White, Padding = new Padding(12) };
            tabs.TabPages.AddRange(new[] { settingsTab, historyTab, ignoredTab });
            root.Controls.Add(tabs, 0, 2);

            TableLayoutPanel settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settingsTab.Controls.Add(settings);
            settings.Controls.Add(Visuals.Label("建议安装根目录", 11, true), 0, 0);
            TableLayoutPanel pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            preferred = new TextBox { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 11), Margin = new Padding(0, 3, 12, 3), AccessibleName = "建议安装根目录" };
            Button browse = Visuals.Button("选择目录…", false);
            browse.MinimumSize = new Size(100, 34);
            browse.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择软件安装的根目录，例如 D:\\Apps", ShowNewFolderButton = true })
                {
                    if (Directory.Exists(preferred.Text)) dialog.SelectedPath = preferred.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) preferred.Text = dialog.SelectedPath;
                }
            };
            pathRow.Controls.Add(preferred, 0, 0);
            pathRow.Controls.Add(browse, 1, 0);
            settings.Controls.Add(pathRow, 0, 1);
            Label hint = Visuals.Label("建议路径会自动补上软件名，例如 D:\\Apps\\软件名称。", 9, false);
            hint.ForeColor = Visuals.Muted;
            hint.MaximumSize = new Size(700, 0);
            settings.Controls.Add(hint, 0, 2);
            FlowLayoutPanel channelRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            Label channelLabel = Visuals.Label("提醒方式", 11, true);
            channelLabel.Margin = new Padding(0, 5, 16, 0);
            channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, AccessibleName = "提醒方式", Margin = new Padding(0, 3, 0, 0) };
            channel.Items.AddRange(new object[] { "弹窗提醒", "Windows 原生通知" });
            channelRow.Controls.Add(channelLabel);
            channelRow.Controls.Add(channel);
            settings.Controls.Add(channelRow, 0, 3);
            channelHint = Visuals.Label("", 9, false);
            channelHint.MaximumSize = new Size(700, 0);
            channelHint.ForeColor = Visuals.Muted;
            settings.Controls.Add(channelHint, 0, 4);
            enabled = new CheckBox { Text = "启用安装提醒", AutoSize = true };
            sound = new CheckBox { Text = "弹窗提示音", AutoSize = true };
            startup = new CheckBox { Text = "登录 Windows 后自动启动", AutoSize = true, Enabled = !context.TestDataDirectory };
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            foreach (CheckBox option in new[] { enabled, sound, startup }) { option.Margin = new Padding(0, 5, 20, 0); options.Controls.Add(option); }
            settings.Controls.Add(options, 0, 5);
            channel.SelectedIndexChanged += delegate { UpdateChannelHint(); };
            saved = Visuals.Label("修改后点击“保存设置”。“测试提醒”可预览当前选择。", 9, false);
            saved.ForeColor = Visuals.Muted;
            settings.Controls.Add(saved, 0, 6);

            history = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false };
            history.Columns.Add("时间", 140);
            history.Columns.Add("安装程序", 225);
            history.Columns.Add("状态", 150);
            history.Columns.Add("识别依据", 280);
            historyTab.Controls.Add(history);
            Label historyNote = Visuals.Label("双击记录查看详情、复制完整路径。仅保存最近 200 条。", 9, false);
            historyNote.Dock = DockStyle.Bottom;
            historyNote.Padding = new Padding(0, 8, 0, 0);
            historyTab.Controls.Add(historyNote);
            history.ShowItemToolTips = true;
            history.ItemActivate += delegate
            {
                if (history.SelectedItems.Count == 1) context.ShowHistoryDetail(history.SelectedItems[0].Tag as HistoryEntry);
            };

            ignored = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
            ignoredTab.Controls.Add(ignored);
            FlowLayoutPanel ignoreBottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(0, 8, 0, 0) };
            Button restore = Visuals.Button("恢复所选提醒", false);
            restore.Click += delegate
            {
                string selected = ignored.SelectedItem as string;
                if (selected == null) return;
                context.Settings.IgnoredExecutables.Remove(selected);
                try { context.Store.SaveSettings(context.Settings); RefreshLists(); }
                catch (Exception ex) { context.Settings.IgnoredExecutables.Add(selected); Visuals.Error(this, ex.Message); }
            };
            ignoreBottom.Controls.Add(restore);
            ignoredTab.Controls.Add(ignoreBottom);
            Label ignoredNote = Visuals.Label("提醒窗口中可忽略某个安装程序，按完整文件路径匹配。", 9, false);
            ignoredNote.Dock = DockStyle.Top;
            ignoredNote.Height = 30;
            ignoredTab.Controls.Add(ignoredNote);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            Button save = Visuals.Button("保存设置", true);
            save.Click += delegate { Save(); };
            Button test = Visuals.Button("测试提醒", false);
            test.Click += delegate
            {
                string normalized, error;
                if (!PathRules.TryValidate(preferred.Text, out normalized, out error)) { Visuals.Error(this, error); return; }
                context.TestReminder(SelectedChannel, normalized, sound.Checked);
            };
            Button hide = Visuals.Button("收起到托盘", false);
            hide.Click += delegate { Hide(); };
            Button exit = Visuals.Button("退出程序", false);
            exit.Click += delegate { context.Quit(); };
            buttons.Controls.AddRange(new Control[] { save, test, hide, exit });
            root.Controls.Add(buttons, 0, 3);
            Label foot = Visuals.Label("提醒助手不会自动修改安装路径。部分安装器不支持选择目录。", 9, false);
            foot.ForeColor = Visuals.Muted;
            root.Controls.Add(foot, 0, 4);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            Shown += delegate
            {
                float scale;
                using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
                int[] widths = { 140, 190, 150, 270 };
                for (int i = 0; i < widths.Length; i++) history.Columns[i].Width = (int)(widths[i] * scale);
            };
            LoadSettings();
            RefreshLists();
            ResumeLayout(false);
            PerformLayout();
        }

        private FlowLayoutPanel Vertical() { return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) }; }

        private string SelectedChannel { get { return channel.SelectedIndex == 1 ? ReminderChannels.Windows : ReminderChannels.Popup; } }

        private void UpdateChannelHint()
        {
            bool native = SelectedChannel == ReminderChannels.Windows;
            sound.Enabled = !native;
            sound.Text = native ? "声音由 Windows 控制" : "弹窗提示音";
            channelHint.Text = native
                ? "通知中可一键复制完整路径，点击正文查看对应软件详情。\n横幅与声音受系统通知设置、专注助手控制。"
                : "弹窗会显示含软件名的完整路径，可调整路径后再一键复制。";
        }

        internal void ShowHistory()
        {
            tabs.SelectedTab = historyTab;
            history.Focus();
        }

        internal void ShowCopyStatus(string path)
        {
            saved.ForeColor = Visuals.Green;
            saved.Text = "已复制建议安装路径，可在安装向导中粘贴。";
        }

        internal void LoadSettings()
        {
            preferred.Text = context.Settings.PreferredPath;
            enabled.Checked = context.Settings.NotificationsEnabled;
            sound.Checked = context.Settings.SoundEnabled;
            startup.Checked = context.Settings.StartWithWindows;
            channel.SelectedIndex = ReminderChannels.Normalize(context.Settings.ReminderChannel) == ReminderChannels.Windows ? 1 : 0;
            UpdateChannelHint();
        }

        private void Save()
        {
            string normalized, error;
            if (!PathRules.TryValidate(preferred.Text, out normalized, out error)) { Visuals.Error(this, error); preferred.Focus(); return; }
            AppSettings next = new AppSettings { PreferredPath = normalized, NotificationsEnabled = enabled.Checked, SoundEnabled = sound.Checked, StartWithWindows = startup.Checked, ReminderChannel = SelectedChannel, IgnoredExecutables = new System.Collections.Generic.List<string>(context.Settings.IgnoredExecutables) };
            try
            {
                context.SaveSettings(next);
                preferred.Text = normalized;
                saved.ForeColor = Visuals.Green;
                saved.Text = "设置已保存 · " + ReminderChannels.DisplayName(next.ReminderChannel) + (next.NotificationsEnabled ? "。正在等待新的安装程序。" : "。提醒已暂停。");
            }
            catch (Exception ex) { Visuals.Error(this, "保存失败：" + ex.Message); }
        }

        internal void RefreshStatus()
        {
            status.Text = !context.Settings.NotificationsEnabled ? "● 提醒已暂停" : (context.MonitorReady ? "● 正在监测安装程序" : "● 监测尚未就绪");
            status.ForeColor = context.Settings.NotificationsEnabled ? Visuals.Green : Visuals.Muted;
            detail.Text = context.MonitorStatus + " · " + ReminderChannels.DisplayName(context.Settings.ReminderChannel);
        }

        internal void RefreshLists()
        {
            history.BeginUpdate();
            history.Items.Clear();
            try
            {
                foreach (HistoryEntry entry in context.Store.LoadHistory())
                {
                    ListViewItem item = new ListViewItem(new[] { entry.Time.ToString("MM-dd HH:mm:ss"), entry.Application ?? "", entry.Action ?? "", entry.Reason ?? "" });
                    item.ToolTipText = entry.ExecutablePath ?? "";
                    item.Tag = entry;
                    history.Items.Add(item);
                }
            }
            catch { }
            history.EndUpdate();
            ignored.Items.Clear();
            foreach (string path in context.Settings.IgnoredExecutables) ignored.Items.Add(path);
        }
    }
}
