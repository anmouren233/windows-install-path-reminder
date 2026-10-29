using System;
using System.Drawing;
using System.Windows.Forms;

namespace InstallPathReminder
{
    internal sealed class ReminderWindow : Form
    {
        internal ReminderWindow(ReminderContext context, PendingReminder item)
        {
            SuspendLayout();
            Text = item.FromHistory ? "安装路径提醒 · 记录详情" : (item.Test ? "安装路径提醒 · 测试" : "安装路径提醒");
            Icon = Visuals.AppIcon;
            Font = new Font("Microsoft YaHei UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            BackColor = Color.White;
            ForeColor = Visuals.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 475);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 8 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);
            Label eyebrow = Visuals.Label(item.FromHistory ? "历史提醒  /  请确认安装是否仍在进行" : (item.Test ? "测试提醒  /  不涉及真实安装" : (item.NeedsReview ? "软件名称待确认  /  可编辑下方路径" : "检测到疑似安装程序")), 10, true);
            eyebrow.ForeColor = Visuals.Green;
            root.Controls.Add(eyebrow, 0, 0);
            root.Controls.Add(Visuals.Label("继续前，检查一下安装位置", 21, true), 0, 1);
            Label program = Visuals.Label(item.SoftwareName + "  ·  " + item.Process.Name + "\n" + item.Reason, 10, false);
            program.AutoSize = false;
            program.Dock = DockStyle.Fill;
            program.AutoEllipsis = true;
            root.Controls.Add(program, 0, 2);
            Panel target = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(234, 245, 239), Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 14) };
            Label caption = Visuals.Label("建议安装路径 · 已添加软件文件夹，可在下方调整", 9, false);
            caption.Dock = DockStyle.Top;
            TextBox path = new TextBox { Text = item.PreferredPath, ReadOnly = false, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold), Dock = DockStyle.Bottom, AccessibleName = "建议安装路径" };
            target.Controls.Add(path);
            target.Controls.Add(caption);
            root.Controls.Add(target, 0, 3);
            Label instructions = Visuals.Label("在安装向导中选择“自定义 / 高级 / 浏览”，再修改路径。\n不要直接连续点击“下一步”或“立即安装”。", 10, false);
            instructions.MaximumSize = new Size(555, 0);
            root.Controls.Add(instructions, 0, 4);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            Button copy = Visuals.Button("一键复制建议安装路径", true);
            copy.Click += delegate
            {
                string error;
                if (context.TryCopySuggestedPath(path.Text, out error)) copy.Text = "已复制到剪贴板";
                else Visuals.Error(this, error);
            };
            path.TextChanged += delegate { copy.Text = "一键复制建议安装路径"; };
            Button okay = Visuals.Button("知道了", false);
            okay.Click += delegate { Close(); };
            actions.Controls.Add(copy);
            actions.Controls.Add(okay);
            root.Controls.Add(actions, 0, 5);
            bool isMsi = String.Equals(item.Process.Name, "msiexec.exe", StringComparison.OrdinalIgnoreCase);
            LinkLabel ignore = new LinkLabel { Text = isMsi ? "MSI 使用系统安装服务，不支持按程序忽略" : "不再提醒这个安装程序", AutoSize = true, LinkColor = Visuals.Muted, Enabled = !item.Test && !isMsi && !String.IsNullOrWhiteSpace(item.Process.ExecutablePath) };
            ignore.LinkClicked += delegate
            {
                try { context.Ignore(item.Process.ExecutablePath); Close(); }
                catch (Exception ex) { Visuals.Error(this, "无法保存忽略设置：" + ex.Message); }
            };
            root.Controls.Add(ignore, 0, 6);
            Label note = Visuals.Label("本工具仅提醒，不会暂停安装，也不会自动修改路径。", 9, false);
            note.ForeColor = Visuals.Muted;
            root.Controls.Add(note, 0, 7);
            CancelButton = okay;
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
