using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UnrealPluginsGUI.Views
{
    public partial class AboutWindow : Window
    {
        private const string TelegramUrl = "https://t.me/yenchzk_archive";
        private const string GitHubUrl = "https://github.com/yenichw";

        public AboutWindow()
        {
            InitializeComponent();
        }

        private void OpenTelegram_OnClick(object? sender, RoutedEventArgs e)
        {
            OpenUrl(TelegramUrl);
        }

        private void OpenGitHub_OnClick(object? sender, RoutedEventArgs e)
        {
            OpenUrl(GitHubUrl);
        }

        private void Close_OnClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Intentionally ignore.
            }
        }
    }
}
