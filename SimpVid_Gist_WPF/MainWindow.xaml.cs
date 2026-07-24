using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeExplode;


namespace SimpVid_Gist_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly YoutubeClient _youtubeClient = new YoutubeClient();
        private readonly HttpClient _httpClient = new HttpClient();
        public MainWindow()
        {
            InitializeComponent();

            // Load previous user configurations
            LoadFromAppData();
        }

        private async void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            string videoInput = UrlTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(videoInput))
            {
                MessageBox.Show("Please enter a valid YouTube Video URL or ID", "Invalid Video Input", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // UI Feedback while working
            ExtractButton.IsEnabled = false;
            TranscriptTextBox.Text = "Fetching transcript tracks...";
            try
            {
                // 1. Fetch the caption tracks available for the video
                var trackManifest = await _youtubeClient.Videos.ClosedCaptions.GetManifestAsync(videoInput);

                // 2. Grab track with specified language
                string targetLangCode = (LanguageCodeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";

                var trackInfo = trackManifest.GetByLanguage(targetLangCode)
                                ?? trackManifest.Tracks.FirstOrDefault();


                if (trackInfo != null)
                {
                    TranscriptTextBox.Text = "Downloading transcript...";

                    // 3. Download the actual transcript contents
                    var closedCaptionTrack = await _youtubeClient.Videos.ClosedCaptions.GetAsync(trackInfo);

                    // 4. Compile the text segments together
                    var transcriptBuilder = new StringBuilder();
                    foreach (var caption in closedCaptionTrack.Captions)
                    {
                        // Exclude empty entries or music tags like [Music] if desired
                        if (!string.IsNullOrWhiteSpace(caption.Text))
                        {
                            transcriptBuilder.AppendLine(caption.Text);
                        }
                    }

                    // 5. Output to the UI
                    TranscriptTextBox.Text = transcriptBuilder.ToString();

                    SummarizeButton.IsEnabled = true;
                }
                else
                {
                    TranscriptTextBox.Text = $"No transcript or captions found for this video in language {targetLangCode}.";
                }
            }
            catch (Exception ex)
            {
                TranscriptTextBox.Text = $"Error retrieving transcript: {ex.Message}";
            }
            finally
            {
                // Always re-enable the button when done
                ExtractButton.IsEnabled = true;
            }
        }
        /// <summary>
        /// Step 2: Handle Self-service AI summarization
        /// </summary>

        private async void SummarizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Securely grab the key from PasswordBox
            string apiKey = ApiKeyTextBox.Password.Trim();
            string transcript = TranscriptTextBox.Text.Trim();
            string apiUrl = BaseUrlTextBox.Text.Trim();
            string modelName = ModelTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("Please enter your AI API key.", "AI API key Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(transcript) || transcript.StartsWith("Error"))
            {
                MessageBox.Show("Please fetch a valid video transcript before summarizing.", "Transcript Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                MessageBox.Show("Please enter the AI Base URL.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                MessageBox.Show("Please enter the Model Name.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Lock UI elements during network traffic
            SummarizeButton.IsEnabled = false;
            ExtractButton.IsEnabled = false;
            SummaryTextBox.Text = "Analyzing transcript & generating summary...";

            try
            {
                string summaryResult = await CallAiApiAsync(apiKey, transcript, apiUrl, modelName);
                SummaryTextBox.Text = summaryResult;
            }
            catch (Exception ex)
            {
                SummaryTextBox.Text = $"AI Error: {ex.Message}";
            }
            finally
            {
                SummarizeButton.IsEnabled = true;
                ExtractButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Universal HTTP wrapper compatible with standard AI endpoints
        /// </summary>
        private async Task<string> CallAiApiAsync(string apiKey, string transcriptContent, string apiUrl, string modelName)
        {
            // 使用 JsonObject 避免反射序列化异常
            var requestBody = new JsonObject
            {
                ["model"] = modelName,
                ["messages"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = "You are an expert assistant. Summarize the following YouTube transcript into clear, structured paragraphs. You may use key bullet points."
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = transcriptContent
            }
        },
                ["temperature"] = 0.5
            };

            string jsonPayload = requestBody.ToJsonString();

            using (var request = new HttpRequestMessage(HttpMethod.Post, apiUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"({response.StatusCode}) Details: {responseString}");
                }

                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    JsonElement root = doc.RootElement;
                    string rawSummary = root.GetProperty("choices")[0]
                                            .GetProperty("message")
                                            .GetProperty("content")
                                            .GetString();
                    return rawSummary?.Trim() ?? "AI returned an empty response.";
                }
            }
        }

        private void Button_Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DockPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }


        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string dataToSave = BaseUrlTextBox.Text + "\n" + ModelTextBox.Text;
            SaveToAppData("userdata.txt", dataToSave);
        }
        /// <summary>
        /// Save user configurations
        /// </summary>

        private void SaveToAppData(string fileName, string content)
        {
            try
            {
                // 获取 AppData\Roaming 路径
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // 拼接你自己的应用程序文件夹名称（防止污染根目录）
                string myAppFolder = System.IO.Path.Combine(appDataPath, "SimpVid Gist");

                // 检查文件夹是否存在，如果不存在则创建
                if (!Directory.Exists(myAppFolder))
                {
                    Directory.CreateDirectory(myAppFolder);
                }

                // 拼接完整的文件路径
                string filePath = System.IO.Path.Combine(myAppFolder, fileName);

                // 写入文件（此处使用 UTF-8 编码，若文件已存在则覆盖）
                File.WriteAllText(filePath, content, Encoding.UTF8);
                MessageBox.Show($"Successfully saved.\nAt: {filePath}\nThe next time you open SimpVid Gist, your data will be automatically read.", "", MessageBoxButton.OK, MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// Read user configurations
        /// </summary>
        private void LoadFromAppData()
        {
            try
            {
                // Get & Combine Path
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string filePath = System.IO.Path.Combine(appDataPath, "SimpVid Gist", "userdata.txt");

                // If file exists, read the configuration.
                if (File.Exists(filePath))
                {
                    // Read the file by line
                    string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);

                    // Fill in textboxes
                    if (lines.Length >= 1)
                    {
                        BaseUrlTextBox.Text = lines[0];
                    }
                    if (lines.Length >= 2)
                    {
                        ModelTextBox.Text = lines[1];
                    }

                }
            }
            catch (Exception ex)
            {
                // What to do if load failed.
                MessageBox.Show($"Failed to load save data:\n{ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WriteInContent()
        {
            string transcript = TranscriptTextBox.Text.Trim();
            string summary = SummaryTextBox.Text.Trim();

            // Initialize file save dialog box.
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
            saveFileDialog.DefaultExt = "txt";
            saveFileDialog.FileName = "SimpVid_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // If user clicked save...
            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Put together two parts.
                    StringBuilder fileContent = new StringBuilder();
                    fileContent.AppendLine("========================================");
                    fileContent.AppendLine("           YOUTUBE TRANSCRIPT           ");
                    fileContent.AppendLine("========================================");
                    fileContent.AppendLine(string.IsNullOrWhiteSpace(transcript) ? "[No Transcript Available]" : transcript);

                    if (!string.IsNullOrEmpty(summary))
                    {
                        fileContent.AppendLine();
                        fileContent.AppendLine("========================================");
                        fileContent.AppendLine("               AI SUMMARY               ");
                        fileContent.AppendLine("========================================");
                        fileContent.AppendLine(string.IsNullOrWhiteSpace(summary) ? "[No Summary Generated]" : summary);
                    }

                    // Write text to user-specified file path.
                    File.WriteAllText(saveFileDialog.FileName, fileContent.ToString(), Encoding.UTF8);

                    MessageBox.Show($"File Successfully Saved to\n{saveFileDialog.FileName}", "", MessageBoxButton.OK, MessageBoxImage.Information);

                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export file: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

        }

        /// <summary>
        /// Save to *.txt file.
        /// </summary>

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            // If two fields are empty, do not export.
            if (!SummarizeButton.IsEnabled)
            {
                MessageBox.Show("Please extract transcript first", "Nothing Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WriteInContent();
            }

        }
    }
}