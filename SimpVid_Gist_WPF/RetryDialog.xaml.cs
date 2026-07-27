using System.Windows;

namespace SimpVid_Gist_WPF
{
    public partial class RetryDialog : Window
    {
        public bool IsAutoRetry { get; private set; } = false;

        public RetryDialog(string langCode, bool zh)
        {
            InitializeComponent();
            Title = zh ? "字幕获取提示" : "Transcript Notice";
            MessageText.Text = zh
                ? $"未找到语言 \"{langCode}\" 的字幕。请选择操作："
                : $"No transcript found for language \"{langCode}\". Choose an option:";
            AutoRetryButton.Content = zh ? "自动重试（尝试所有可用语言，较慢）" : "Auto-retry (try all available languages, slower)";
            ManualRetryButton.Content = zh ? "手动重试（返回并重新选择语言）" : "Manual retry (go back and change language)";
        }

        private void AutoRetryButton_Click(object sender, RoutedEventArgs e)
        {
            IsAutoRetry = true;
            DialogResult = true;
        }

        private void ManualRetryButton_Click(object sender, RoutedEventArgs e)
        {
            IsAutoRetry = false;
            DialogResult = true;
        }
    }
}
