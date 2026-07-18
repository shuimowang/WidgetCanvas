#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace WidgetCanvas.Services
{
    /// <summary>
    /// 使用当前 Windows 用户的 DPAPI 保存敏感设置。密文只能由同一用户解开，
    /// 不会随着组件数据同步到 WebDAV。
    /// </summary>
    internal static class SecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WidgetCanvas.WebDav.v1");

        public static string Protect(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            byte[] plain = Encoding.UTF8.GetBytes(value);
            try
            {
                byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        public static string Unprotect(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try
            {
                byte[] encrypted = Convert.FromBase64String(value);
                byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    return Encoding.UTF8.GetString(plain);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                throw new InvalidOperationException("WebDAV 密码无法由当前 Windows 用户解密，请重新输入。", ex);
            }
        }
    }
}
