#nullable enable

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace WidgetCanvas
{
    internal sealed class TrayIconService : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private readonly System.Windows.Forms.ContextMenuStrip _menu;

        public TrayIconService(Action showCanvas, Action exitApplication)
        {
            ArgumentNullException.ThrowIfNull(showCanvas);
            ArgumentNullException.ThrowIfNull(exitApplication);

            _menu = new System.Windows.Forms.ContextMenuStrip();
            _menu.Items.Add("打开浮岛", null, (_, _) => showCanvas());
            _menu.Items.Add("打开组件目录", null, (_, _) => OpenFolder(AppPaths.ComponentsFolder));
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add("退出 WidgetCanvas", null, (_, _) => exitApplication());

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = LoadApplicationIcon(),
                Text = "WidgetCanvas · 浮岛",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => showCanvas();
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
        }

        private static Icon LoadApplicationIcon()
        {
            string? processPath = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(processPath)
                ? Icon.ExtractAssociatedIcon(processPath) ?? SystemIcons.Application
                : SystemIcons.Application;
        }

        private static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }
}
