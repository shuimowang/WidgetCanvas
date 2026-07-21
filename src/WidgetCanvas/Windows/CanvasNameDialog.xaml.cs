#nullable enable

using System.Windows;
using System.Windows.Input;

namespace WidgetCanvas.Windows
{
    public partial class CanvasNameDialog : Window
    {
        public CanvasNameDialog(string title, string initialName = "")
        {
            InitializeComponent();
            TitleText.Text = title;
            NameBox.Text = initialName;
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        public string CanvasName => NameBox.Text.Trim();

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (CanvasName.Length == 0)
                return;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void NameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmButton_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }
    }
}
