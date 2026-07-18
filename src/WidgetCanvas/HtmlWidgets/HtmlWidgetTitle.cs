#nullable enable

using System;
using System.Net;

namespace WidgetCanvas.HtmlWidgets
{
    internal static class HtmlWidgetTitle
    {
        public static string GetDisplayName(HtmlWidgetDefinition widget)
        {
            ArgumentNullException.ThrowIfNull(widget);
            string title = GetHtmlTitle(widget.Html);
            return string.IsNullOrWhiteSpace(title) ? "未命名组件" : title;
        }

        public static string GetHtmlTitle(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;
            int titleStart = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
            if (titleStart < 0)
                return string.Empty;
            titleStart = html.IndexOf('>', titleStart);
            if (titleStart < 0)
                return string.Empty;
            int titleEnd = html.IndexOf("</title>", titleStart + 1, StringComparison.OrdinalIgnoreCase);
            if (titleEnd <= titleStart)
                return string.Empty;
            return WebUtility.HtmlDecode(html[(titleStart + 1)..titleEnd]).Trim();
        }

        public static string SetHtmlTitle(string html, string title)
        {
            int titleStart = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
            if (titleStart < 0)
                return html;
            titleStart = html.IndexOf('>', titleStart);
            if (titleStart < 0)
                return html;
            int titleEnd = html.IndexOf("</title>", titleStart + 1, StringComparison.OrdinalIgnoreCase);
            if (titleEnd <= titleStart)
                return html;
            return html[..(titleStart + 1)] +
                   WebUtility.HtmlEncode(title) +
                   html[titleEnd..];
        }
    }
}
