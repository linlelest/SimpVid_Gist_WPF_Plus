using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeExplode;
using YoutubeExplode.Videos.ClosedCaptions;

namespace SimpVid_Gist_WPF
{
    public partial class MainWindow : Window
    {
        private readonly YoutubeClient _youtubeClient = new YoutubeClient();
        private readonly HttpClient _httpClient = new HttpClient();
        private List<ClosedCaption> _captions = new List<ClosedCaption>();
        private string _fullTranscript = "";
        private bool _isExpanded = false;
        private const int MaxPreviewLines = 5;

        private static readonly string[] AllLangCodes = { "en", "zh-Hans", "zh-Hant", "ja", "ko", "es", "fr", "ru" };

        private class ExportFormatItem
        {
            public string Display { get; set; } = "";
            public string Value { get; set; } = "txt";
        }

        private class SummaryLengthItem
        {
            public string Display { get; set; } = "";
            public int? WordCount { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            CheckFirstRun();
            PopulateLanguageCodes();
            PopulateExportFormats();
            PopulateSummaryLengths();
            ApplyLanguage();
            LoadFromAppData();
            BaseUrlPlaceholder.Visibility = string.IsNullOrEmpty(BaseUrlTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PopulateLanguageCodes()
        {
            bool zh = Localization.IsChinese;
            LanguageCodeComboBox.Items.Clear();
            LanguageCodeComboBox.Items.Add(new ComboBoxItem { Content = zh ? "英语 (en)" : "English (en)", Tag = "en" });
            LanguageCodeComboBox.Items.Add(new ComboBoxItem { Content = zh ? "中文 (zh)" : "Chinese (zh)", Tag = "zh" });
            LanguageCodeComboBox.Items.Add(new ComboBoxItem { Content = zh ? "日语 (ja)" : "Japanese (ja)", Tag = "ja" });
            LanguageCodeComboBox.Items.Add(new ComboBoxItem { Content = zh ? "韩语 (ko)" : "Korean (ko)", Tag = "ko" });
            LanguageCodeComboBox.Items.Add(new ComboBoxItem { Content = zh ? "西班牙语 (es)" : "Spanish (es)", Tag = "es" });
            LanguageCodeComboBox.Items.Add(new ComboBoxItem { Content = zh ? "法语 (fr)" : "French (fr)", Tag = "fr" });
            LanguageCodeComboBox.Items.Add(new ComboBoxItem { Content = zh ? "俄语 (ru)" : "Russian (ru)", Tag = "ru" });
            LanguageCodeComboBox.SelectedIndex = 0;
        }

        private void PopulateExportFormats()
        {
            bool zh = Localization.IsChinese;
            ExportFormatComboBox.ItemsSource = new List<ExportFormatItem>
            {
                new() { Display = zh ? "TXT（纯文本）" : "TXT (text only)", Value = "txt" },
                new() { Display = zh ? "SRT（带时间轴）" : "SRT (with timestamps)", Value = "srt" },
            };
            ExportFormatComboBox.DisplayMemberPath = "Display";
            ExportFormatComboBox.SelectedIndex = 0;
        }

        private void PopulateSummaryLengths()
        {
            bool zh = Localization.IsChinese;
            SummaryLengthComboBox.ItemsSource = new List<SummaryLengthItem>
            {
                new() { Display = zh ? "极简（100字）" : "Minimal (100 chars)", WordCount = 100 },
                new() { Display = zh ? "简短（300字）" : "Short (300 chars)", WordCount = 300 },
                new() { Display = zh ? "中等（500字）" : "Medium (500 chars)", WordCount = 500 },
                new() { Display = zh ? "详细（1000字）" : "Long (1000 chars)", WordCount = 1000 },
                new() { Display = zh ? "自定义" : "Custom", WordCount = null },
            };
            SummaryLengthComboBox.DisplayMemberPath = "Display";
            SummaryLengthComboBox.SelectedIndex = 2;
        }

        private void ApplyLanguage()
        {
            bool zh = Localization.IsChinese;

            Title = "SimpVid Gist";
            LangToggleButton.Content = zh ? "EN" : "中";
            Button_Save.Content = zh ? "保存" : "Save";
            Button_Close.Content = zh ? "关闭" : "Close";

            DescriptionText.Text = zh
                ? "提取并总结YouTube视频字幕。提供AI的API密钥以生成摘要。"
                : "Extract and summarize YouTube video transcripts. Provide an AI API key to also generate a summary.";

            UrlLabel.Text = zh ? "视频链接或ID" : "Video URL or ID";
            SubtitleLangLabel.Text = zh ? "字幕语言" : "Subtitle Language";
            ExtractButton.Content = zh ? "提取字幕" : "Extract Transcript";
            TranscriptLabel.Text = zh ? "字幕" : "Transcript";
            ExportFormatLabel.Text = zh ? "导出格式" : "Export Format";
            ExportButton.Content = zh ? "导出" : "Export";

            SummarizationGroupBox.Header = zh ? "AI总结" : "Summarization";
            BaseUrlLabel.Text = zh ? "AI接口地址" : "AI Base URL (API Endpoint)";
            BaseUrlPlaceholder.Text = zh ? "例: https://api.openai.com/v1" : "e.g. https://api.openai.com/v1";
            BaseUrlPlaceholder.Visibility = string.IsNullOrEmpty(BaseUrlTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ModelNameLabel.Text = zh ? "模型名称" : "Model Name";
            ApiKeyLabel.Text = zh ? "API密钥" : "AI API key";
            SummaryLengthLabel.Text = zh ? "总结长度" : "Summary Length";
            SaveButton.Content = zh ? "保存AI配置" : "Save AI Base URL and Model Name";
            SummarizeButton.Content = zh ? "总结字幕" : "Summarize Transcript";
            SummaryLabel.Text = zh ? "总结" : "Summary";

            int langIdx = LanguageCodeComboBox.SelectedIndex;
            int expIdx = ExportFormatComboBox.SelectedIndex;
            int sumIdx = SummaryLengthComboBox.SelectedIndex;

            PopulateLanguageCodes();
            PopulateExportFormats();
            PopulateSummaryLengths();

            if (langIdx >= 0 && langIdx < LanguageCodeComboBox.Items.Count)
                LanguageCodeComboBox.SelectedIndex = langIdx;

            if (expIdx >= 0 && expIdx < ExportFormatComboBox.Items.Count)
                ExportFormatComboBox.SelectedIndex = expIdx;
            if (sumIdx >= 0 && sumIdx < SummaryLengthComboBox.Items.Count)
                SummaryLengthComboBox.SelectedIndex = sumIdx;

            bool isCustom = (SummaryLengthComboBox.SelectedItem as SummaryLengthItem)?.WordCount == null;
            CustomLengthTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LangToggleButton_Click(object sender, RoutedEventArgs e)
        {
            Localization.IsChinese = !Localization.IsChinese;
            ApplyLanguage();
        }

        private void SummaryLengthComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            bool isCustom = (SummaryLengthComboBox.SelectedItem as SummaryLengthItem)?.WordCount == null;
            CustomLengthTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        private int GetWordCountLimit()
        {
            var item = SummaryLengthComboBox.SelectedItem as SummaryLengthItem;
            if (item?.WordCount.HasValue == true)
                return item.WordCount.Value;
            if (int.TryParse(CustomLengthTextBox.Text.Trim(), out int custom) && custom > 0)
                return custom;
            CustomLengthTextBox.Text = "500";
            return 500;
        }

        private void DisplayTranscript(string text)
        {
            _fullTranscript = text;
            _isExpanded = false;
            UpdateTranscriptDisplay();
        }

        private void UpdateTranscriptDisplay()
        {
            if (string.IsNullOrEmpty(_fullTranscript))
            {
                TranscriptTextBox.Text = "";
                ShowMoreButton.Visibility = Visibility.Collapsed;
                return;
            }

            var lines = _fullTranscript.Split('\n');

            if (lines.Length <= MaxPreviewLines)
            {
                TranscriptTextBox.Text = _fullTranscript;
                ShowMoreButton.Visibility = Visibility.Collapsed;
                return;
            }

            ShowMoreButton.Visibility = Visibility.Visible;
            if (_isExpanded)
            {
                TranscriptTextBox.Text = _fullTranscript;
                ShowMoreButton.Content = "▲";
            }
            else
            {
                TranscriptTextBox.Text = string.Join("\n", lines.Take(MaxPreviewLines)) + "\n...";
                ShowMoreButton.Content = "▼";
            }
        }

        private void ShowMoreButton_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;
            ShowMoreButton.Content = _isExpanded ? "▲" : "▼";
            UpdateTranscriptDisplay();
            UpdateLayout();
        }

        private ClosedCaptionTrackInfo? TryGetTrackByCode(ClosedCaptionManifest manifest, string code)
        {
            try
            {
                if (code == "zh")
                {
                    return manifest.GetByLanguage("zh-Hans")
                        ?? manifest.GetByLanguage("zh-Hant")
                        ?? manifest.GetByLanguage("zh-CN")
                        ?? manifest.GetByLanguage("zh-TW");
                }
                return manifest.GetByLanguage(code);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatSrtTime(TimeSpan ts)
        {
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        private async void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            string videoInput = UrlTextBox.Text.Trim();
            bool zh = Localization.IsChinese;

            if (string.IsNullOrWhiteSpace(videoInput))
            {
                MessageBox.Show(zh ? "请输入有效的YouTube链接或ID" : "Please enter a valid YouTube Video URL or ID", zh ? "输入无效" : "Invalid Video Input", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ExtractButton.IsEnabled = false;
            ShowMoreButton.Visibility = Visibility.Collapsed;
            TranscriptTextBox.Text = zh ? "正在验证视频..." : "Validating video...";
            try
            {
                var video = await _youtubeClient.Videos.GetAsync(videoInput);
                TranscriptTextBox.Text = zh ? "正在获取字幕..." : "Fetching transcript tracks...";
                var trackManifest = await _youtubeClient.Videos.ClosedCaptions.GetManifestAsync(video.Id);

                string targetLangCode = (LanguageCodeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";

                var trackInfo = TryGetTrackByCode(trackManifest, targetLangCode);

                if (trackInfo == null && targetLangCode == "zh")
                    trackInfo = trackManifest.Tracks.FirstOrDefault();

                if (trackInfo == null)
                {
                    bool autoRetry = ShowRetryDialog(targetLangCode);
                    if (autoRetry)
                    {
                        foreach (var code in AllLangCodes)
                        {
                            if (code == targetLangCode) continue;
                            if (targetLangCode == "zh" && (code == "zh-Hans" || code == "zh-Hant")) continue;

                            trackInfo = TryGetTrackByCode(trackManifest, code);
                            if (trackInfo != null) break;
                        }

                        trackInfo ??= trackManifest.Tracks.FirstOrDefault();
                    }
                }

                if (trackInfo != null)
                {
                    TranscriptTextBox.Text = zh ? "正在下载字幕..." : "Downloading transcript...";

                    var closedCaptionTrack = await _youtubeClient.Videos.ClosedCaptions.GetAsync(trackInfo);
                    _captions = closedCaptionTrack.Captions.ToList();

                    var transcriptBuilder = new StringBuilder();
                    foreach (var caption in _captions)
                    {
                        if (!string.IsNullOrWhiteSpace(caption.Text))
                        {
                            transcriptBuilder.AppendLine(caption.Text);
                        }
                    }

                    DisplayTranscript(transcriptBuilder.ToString());
                    SummarizeButton.IsEnabled = true;
                }
                else
                {
                    string msg = zh ? "未找到该视频的任何字幕。" : "No captions found for this video.";
                    TranscriptTextBox.Text = msg;
                }
            }
            catch (HttpRequestException)
            {
                string msg = zh ? "网络连接失败，请检查网络设置。" : "Network connection failed. Please check your internet connection.";
                MessageBox.Show(msg, zh ? "网络错误" : "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TranscriptTextBox.Text = "";
            }
            catch (TaskCanceledException)
            {
                string msg = zh ? "请求超时，请稍后重试。" : "Request timed out. Please try again.";
                MessageBox.Show(msg, zh ? "超时" : "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                TranscriptTextBox.Text = "";
            }
            catch (Exception ex)
            {
                string msg = zh ? $"获取字幕时出错: {ex.Message}" : $"Error retrieving transcript: {ex.Message}";
                MessageBox.Show(msg, zh ? "错误" : "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TranscriptTextBox.Text = "";
            }
            finally
            {
                ExtractButton.IsEnabled = true;
            }
        }

        private async void SummarizeButton_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = ApiKeyTextBox.Password.Trim();
            string transcript = _fullTranscript.Trim();
            string apiUrl = BaseUrlTextBox.Text.Trim();
            string modelName = ModelTextBox.Text.Trim();
            bool zh = Localization.IsChinese;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show(zh ? "请输入AI API密钥。" : "Please enter your AI API key.", zh ? "需要API密钥" : "AI API key Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(transcript))
            {
                MessageBox.Show(zh ? "请先提取有效的视频字幕。" : "Please fetch a valid video transcript before summarizing.", zh ? "字幕为空" : "Transcript Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                MessageBox.Show(zh ? "请输入AI接口地址。" : "Please enter the AI Base URL.", zh ? "请输入" : "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(modelName))
            {
                MessageBox.Show(zh ? "请输入模型名称。" : "Please enter the Model Name.", zh ? "请输入" : "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SummarizeButton.IsEnabled = false;
            ExtractButton.IsEnabled = false;
            SummaryTextBox.Text = zh ? "正在分析字幕并生成总结..." : "Analyzing transcript & generating summary...";

            try
            {
                int wordLimit = GetWordCountLimit();
                string summaryResult = await CallAiApiAsync(apiKey, transcript, apiUrl, modelName, wordLimit);
                SummaryTextBox.Text = summaryResult;
            }
            catch (Exception ex)
            {
                SummaryTextBox.Text = zh ? $"AI错误: {ex.Message}" : $"AI Error: {ex.Message}";
            }
            finally
            {
                SummarizeButton.IsEnabled = true;
                ExtractButton.IsEnabled = true;
            }
        }

        private async Task<string> CallAiApiAsync(string apiKey, string transcriptContent, string apiUrl, string modelName, int wordLimit)
        {
            bool zh = Localization.IsChinese;

            if (!apiUrl.EndsWith("/chat/completions"))
                apiUrl = apiUrl.TrimEnd('/') + "/chat/completions";

            string systemPrompt = zh
                ? $"你是一个专业的AI总结助手。请将以下YouTube字幕总结为清晰、有条理的段落。可以使用要点。\n\n【硬性约束】总结必须精确输出{wordLimit}个字，不得多不得少！你必须精确计数，输出字数必须严格等于{wordLimit}。如果字数不符，请调整内容直到完全匹配。三项原则：1)输出精确{wordLimit}字 2)保留核心信息 3)语言简洁精炼。\n\n直接输出总结内容，无需任何额外说明。"
                : $"You are an expert AI summarizer. Summarize the following YouTube transcript into clear, structured paragraphs. You may use bullet points.\n\n【STRICT CONSTRAINT】The summary must be EXACTLY {wordLimit} characters — no more, no less! You MUST count precisely and output exactly {wordLimit} characters. If the count doesn't match, adjust the content until it does. Three rules: 1) Output exactly {wordLimit} characters 2) Keep all key information 3) Be concise.\n\nOutput the summary directly without any additional notes.";

            var requestBody = new JsonObject
            {
                ["model"] = modelName,
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "system",
                        ["content"] = systemPrompt
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
                    return rawSummary?.Trim() ?? (zh ? "AI返回了空响应。" : "AI returned an empty response.");
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

        private void SaveToAppData(string fileName, string content)
        {
            bool zh = Localization.IsChinese;
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string myAppFolder = System.IO.Path.Combine(appDataPath, "SimpVid Gist");
                if (!Directory.Exists(myAppFolder))
                {
                    Directory.CreateDirectory(myAppFolder);
                }
                string filePath = System.IO.Path.Combine(myAppFolder, fileName);
                File.WriteAllText(filePath, content, Encoding.UTF8);
                MessageBox.Show(zh
                    ? $"保存成功。\n路径: {filePath}\n下次打开SimpVid Gist时将自动读取。"
                    : $"Successfully saved.\nAt: {filePath}\nThe next time you open SimpVid Gist, your data will be automatically read.", "", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(zh ? $"保存失败: {ex.Message}" : $"Failed to save: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ShowRetryDialog(string langCode)
        {
            bool zh = Localization.IsChinese;

            var dialog = new Window
            {
                Title = zh ? "字幕获取提示" : "Transcript Notice",
                Width = 420,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(16) };

            panel.Children.Add(new TextBlock
            {
                Text = zh
                    ? $"未找到语言 \"{langCode}\" 的字幕。请选择操作："
                    : $"No transcript found for language \"{langCode}\". Choose an option:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            bool result = false;
            var autoBtn = new Button
            {
                Content = zh ? "自动重试（尝试所有可用语言，较慢）" : "Auto-retry (try all available languages, slower)",
                Height = 32,
                Margin = new Thickness(0, 0, 0, 8)
            };
            autoBtn.Click += (_, _) => { result = true; dialog.Close(); };
            panel.Children.Add(autoBtn);

            var manualBtn = new Button
            {
                Content = zh ? "手动重试（返回并重新选择语言）" : "Manual retry (go back and change language)",
                Height = 32
            };
            manualBtn.Click += (_, _) => { result = false; dialog.Close(); };
            panel.Children.Add(manualBtn);

            dialog.Content = panel;
            dialog.ShowDialog();
            return result;
        }

        private void CheckFirstRun()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string myAppFolder = System.IO.Path.Combine(appDataPath, "SimpVid Gist");
            string filePath = System.IO.Path.Combine(myAppFolder, "lang_setting.txt");

            if (File.Exists(filePath))
            {
                string savedLang = File.ReadAllText(filePath, Encoding.UTF8).Trim();
                Localization.IsChinese = savedLang == "zh";
                return;
            }

            var dialog = new Window
            {
                Title = "Select Language / 选择语言",
                Width = 350,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(16) };

            panel.Children.Add(new TextBlock
            {
                Text = "Please select your language / 请选择语言",
                TextAlignment = TextAlignment.Center,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 20)
            });

            string chosenLang = "en";

            var enBtn = new Button
            {
                Content = "English",
                Height = 36,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 14
            };
            enBtn.Click += (_, _) => { chosenLang = "en"; dialog.Close(); };
            panel.Children.Add(enBtn);

            var zhBtn = new Button
            {
                Content = "中文",
                Height = 36,
                FontSize = 14
            };
            zhBtn.Click += (_, _) => { chosenLang = "zh"; dialog.Close(); };
            panel.Children.Add(zhBtn);

            dialog.Content = panel;
            dialog.ShowDialog();

            Localization.IsChinese = chosenLang == "zh";

            if (!Directory.Exists(myAppFolder))
                Directory.CreateDirectory(myAppFolder);
            File.WriteAllText(filePath, chosenLang, Encoding.UTF8);
        }

        private void LoadFromAppData()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string filePath = System.IO.Path.Combine(appDataPath, "SimpVid Gist", "userdata.txt");
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
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
                bool zh = Localization.IsChinese;
                MessageBox.Show(zh ? $"加载保存数据失败:\n{ex.Message}" : $"Failed to load save data:\n{ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void WriteInContent()
        {
            string transcript = TranscriptTextBox.Text.Trim();
            string summary = SummaryTextBox.Text.Trim();
            bool zh = Localization.IsChinese;

            var formatItem = ExportFormatComboBox.SelectedItem as ExportFormatItem;
            bool isSrt = formatItem?.Value == "srt" && _captions.Count > 0;

            SaveFileDialog saveFileDialog = new SaveFileDialog();

            if (isSrt)
            {
                saveFileDialog.Filter = "SRT Files (*.srt)|*.srt|All Files (*.*)|*.*";
                saveFileDialog.DefaultExt = "srt";
            }
            else
            {
                saveFileDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                saveFileDialog.DefaultExt = "txt";
            }
            saveFileDialog.FileName = "SimpVid_Export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    if (isSrt)
                    {
                        StringBuilder sb = new StringBuilder();
                        int seq = 1;
                        foreach (var caption in _captions)
                        {
                            if (!string.IsNullOrWhiteSpace(caption.Text))
                            {
                                string start = FormatSrtTime(caption.Offset);
                                string end = FormatSrtTime(caption.Offset + caption.Duration);
                                sb.AppendLine(seq.ToString());
                                sb.AppendLine($"{start} --> {end}");
                                sb.AppendLine(caption.Text);
                                sb.AppendLine();
                                seq++;
                            }
                        }
                        File.WriteAllText(saveFileDialog.FileName, sb.ToString(), Encoding.UTF8);
                    }
                    else
                    {
                        StringBuilder fileContent = new StringBuilder();
                        fileContent.AppendLine("========================================");
                        fileContent.AppendLine(zh ? "          YouTube 字幕           " : "           YOUTUBE TRANSCRIPT           ");
                        fileContent.AppendLine("========================================");
                        fileContent.AppendLine(string.IsNullOrWhiteSpace(transcript) ? (zh ? "[无字幕]" : "[No Transcript Available]") : transcript);

                        if (!string.IsNullOrEmpty(summary))
                        {
                            fileContent.AppendLine();
                            fileContent.AppendLine("========================================");
                            fileContent.AppendLine(zh ? "               AI 总结               " : "               AI SUMMARY               ");
                            fileContent.AppendLine("========================================");
                            fileContent.AppendLine(summary);
                        }

                        File.WriteAllText(saveFileDialog.FileName, fileContent.ToString(), Encoding.UTF8);
                    }

                    MessageBox.Show(zh
                        ? $"文件已成功保存到\n{saveFileDialog.FileName}"
                        : $"File Successfully Saved to\n{saveFileDialog.FileName}", "", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(zh ? $"导出文件失败: {ex.Message}" : $"Failed to export file: {ex.Message}", zh ? "导出错误" : "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            bool zh = Localization.IsChinese;
            if (!SummarizeButton.IsEnabled)
            {
                MessageBox.Show(zh ? "请先提取字幕" : "Please extract transcript first", zh ? "未保存" : "Nothing Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WriteInContent();
            }
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            string dataToSave = BaseUrlTextBox.Text + "\n" + ModelTextBox.Text;
            SaveToAppData("userdata.txt", dataToSave);
        }

        private void BaseUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            BaseUrlPlaceholder.Visibility = string.IsNullOrEmpty(BaseUrlTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
