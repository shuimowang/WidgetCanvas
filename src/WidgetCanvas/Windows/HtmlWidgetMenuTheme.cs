#nullable enable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WidgetCanvas.Windows
{
    internal static class HtmlWidgetMenuTheme
    {
        public static void Apply(ContextMenu menu)
        {
            menu.Background = new SolidColorBrush(Color.FromRgb(24, 34, 48));
            menu.Foreground = new SolidColorBrush(Color.FromRgb(232, 239, 249));
            menu.BorderBrush = new SolidColorBrush(Color.FromRgb(74, 93, 120));
            menu.BorderThickness = new Thickness(1);
            menu.Padding = new Thickness(4);
            menu.FontFamily = new FontFamily("Microsoft YaHei UI");
            menu.FontSize = 12;
            menu.HasDropShadow = true;
            menu.SnapsToDevicePixels = true;

            var itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, menu.Foreground));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            var highlighted = new Trigger
            {
                Property = MenuItem.IsHighlightedProperty,
                Value = true
            };
            highlighted.Setters.Add(new Setter(
                Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(48, 66, 90))));
            highlighted.Setters.Add(new Setter(
                Control.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(248, 251, 255))));
            itemStyle.Triggers.Add(highlighted);
            menu.Resources[typeof(MenuItem)] = itemStyle;

            var separatorStyle = new Style(typeof(Separator));
            separatorStyle.Setters.Add(new Setter(Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(61, 78, 101))));
            separatorStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 1d));
            separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(7, 4, 7, 4)));
            menu.Resources[typeof(Separator)] = separatorStyle;
        }
    }
}
