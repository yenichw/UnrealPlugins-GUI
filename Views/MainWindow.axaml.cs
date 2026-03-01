using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UnrealPluginsGUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void About_OnClick(object? sender, RoutedEventArgs e)
        {
            var w = new AboutWindow();
            await w.ShowDialog(this);
        }
    }
}
