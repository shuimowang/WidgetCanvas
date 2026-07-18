#nullable enable

using System;
using Microsoft.Win32;

namespace WidgetCanvas.Services
{
    internal static class StartupService
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WidgetCanvas";

        public static bool IsEnabled()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            string? value = key?.GetValue(ValueName) as string;
            return string.Equals(value, GetCommand(), StringComparison.OrdinalIgnoreCase);
        }

        public static void SetEnabled(bool enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            key.SetValue(ValueName, GetCommand(), RegistryValueKind.String);
        }

        private static string GetCommand()
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定 WidgetCanvas 程序路径。");
            return $"\"{executablePath}\" --background";
        }
    }
}
