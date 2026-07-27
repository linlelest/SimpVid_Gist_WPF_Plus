using System.Windows;

namespace SimpVid_Gist_WPF
{
    public partial class LanguagePickerDialog : Window
    {
        public string SelectedLanguage { get; private set; } = "en";

        public LanguagePickerDialog()
        {
            InitializeComponent();
        }

        private void EnglishButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedLanguage = "en";
            DialogResult = true;
        }

        private void ChineseButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedLanguage = "zh";
            DialogResult = true;
        }
    }
}
