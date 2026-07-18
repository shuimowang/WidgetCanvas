#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using WidgetCanvas.HtmlWidgets;

namespace WidgetCanvas
{
    internal sealed class TrayIconService : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private readonly System.Windows.Forms.ContextMenuStrip _menu;
        private readonly System.Windows.Forms.ToolStripMenuItem _canvasMenu;
        private readonly System.Windows.Forms.ToolStripMenuItem _libraryMenu;
        private readonly Action _showCanvas;
        private readonly Action _showLibrary;
        private readonly Action<string> _showWidget;
        private readonly Func<IReadOnlyList<HtmlWidgetTrayEntry>> _getWidgets;

        public TrayIconService(
            Action showCanvas,
            Action showLibrary,
            Action<string> showWidget,
            Func<IReadOnlyList<HtmlWidgetTrayEntry>> getWidgets,
            Action showSettings,
            Action exitApplication)
        {
            ArgumentNullException.ThrowIfNull(showCanvas);
            ArgumentNullException.ThrowIfNull(showLibrary);
            ArgumentNullException.ThrowIfNull(showWidget);
            ArgumentNullException.ThrowIfNull(getWidgets);
            ArgumentNullException.ThrowIfNull(showSettings);
            ArgumentNullException.ThrowIfNull(exitApplication);

            _showCanvas = showCanvas;
            _showLibrary = showLibrary;
            _showWidget = showWidget;
            _getWidgets = getWidgets;

            _menu = new System.Windows.Forms.ContextMenuStrip();
            _canvasMenu = new System.Windows.Forms.ToolStripMenuItem("画布");
            _libraryMenu = new System.Windows.Forms.ToolStripMenuItem("组件库");
            _menu.Items.Add(_canvasMenu);
            _menu.Items.Add(_libraryMenu);
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add("管理中心", null, (_, _) => showSettings());
            _menu.Items.Add("打开组件目录", null, (_, _) => OpenFolder(AppPaths.ComponentsFolder));
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add("退出 WidgetCanvas", null, (_, _) => exitApplication());
            _menu.Opening += (_, _) => RebuildWidgetMenus();
            RebuildWidgetMenus();

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

        private void RebuildWidgetMenus()
        {
            try
            {
                IReadOnlyList<HtmlWidgetTrayEntry> entries = _getWidgets();
                PopulateGroup(
                    _canvasMenu,
                    "画布",
                    "打开画布",
                    _showCanvas,
                    entries.Where(entry => entry.Home == HtmlWidgetHome.Canvas));
                PopulateGroup(
                    _libraryMenu,
                    "组件库",
                    "打开组件库",
                    _showLibrary,
                    entries.Where(entry => entry.Home == HtmlWidgetHome.Library));
            }
            catch (Exception ex)
            {
                PopulateError(_canvasMenu, ex.Message);
                PopulateError(_libraryMenu, ex.Message);
            }
        }

        private void PopulateGroup(
            System.Windows.Forms.ToolStripMenuItem group,
            string groupText,
            string openText,
            Action openGroup,
            IEnumerable<HtmlWidgetTrayEntry> entries)
        {
            ClearDropDown(group);
            HtmlWidgetTrayEntry[] widgets = entries.ToArray();
            group.Text = $"{groupText} ({widgets.Length})";
            group.DropDownItems.Add(openText, null, (_, _) => openGroup());
            group.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());

            if (widgets.Length == 0)
            {
                group.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("暂无组件")
                {
                    Enabled = false
                });
                return;
            }

            foreach (HtmlWidgetTrayEntry widget in widgets)
            {
                string componentName = widget.Name;
                var item = new System.Windows.Forms.ToolStripMenuItem(FormatMenuText(componentName))
                {
                    ToolTipText = componentName
                };
                item.Click += (_, _) => ShowWidget(componentName);
                group.DropDownItems.Add(item);
            }
        }

        private void ShowWidget(string componentName)
        {
            try
            {
                _showWidget(componentName);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "WidgetCanvas",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
        }

        private static void PopulateError(
            System.Windows.Forms.ToolStripMenuItem group,
            string message)
        {
            ClearDropDown(group);
            group.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("组件列表读取失败")
            {
                Enabled = false,
                ToolTipText = message
            });
        }

        private static void ClearDropDown(System.Windows.Forms.ToolStripMenuItem group)
        {
            while (group.DropDownItems.Count > 0)
            {
                System.Windows.Forms.ToolStripItem item = group.DropDownItems[0];
                group.DropDownItems.RemoveAt(0);
                item.Dispose();
            }
        }

        private static string FormatMenuText(string componentName)
        {
            const int maximumLength = 48;
            string text = componentName.Length <= maximumLength
                ? componentName
                : componentName[..(maximumLength - 1)] + "…";
            return text.Replace("&", "&&", StringComparison.Ordinal);
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
