using Microsoft.Win32;
using System.Net;
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
using System.Windows.Media.Animation;
using System.Windows.Threading;
using YoutubeExplode;
using YoutubeExplode.Videos.ClosedCaptions;

namespace SimpVid_Gist_WPF
{
    public partial class MainWindow : Window
    {
        private readonly YoutubeClient _youtubeClient;
        private readonly HttpClient _httpClient;
        private List<ClosedCaption> _captions = new List<ClosedCaption>();
        private string _fullTranscript = "";
        private bool _isExpanded = false;
        private string _fullSummary = "";
        private bool _isSummaryExpanded = false;
        private const int MaxPreviewLines = 5;

        private static readonly string[] AllLangCodes = { "en", "zh-Hans", "zh-Hant", "ja", "ko", "es", "fr", "ru" };

        private enum AiMode { Summary, Translation, KnowledgeGraph }
        private AiMode _currentMode = AiMode.Summary;
        private string _lastMermaidCode = "";

        private Rect _normalBounds;
        private bool _isMaximized;

        private enum LayoutMode { Split, Scroll }
        private LayoutMode _currentLayout = LayoutMode.Scroll;
        private bool _isAnimatingScroll = false;
        private static readonly string LayoutModeFile =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   "SimpVid Gist", "layout_mode.txt");
        private static readonly string LayoutHintFile =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   "SimpVid Gist", "layout_hint_shown.txt");

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
            Logger.Separate();
            Logger.Log("MainWindow constructor start");
            try
            {
                InitializeComponent();
                Logger.Log("InitializeComponent done");

                _httpClient = new HttpClient(new SocketsHttpHandler
                {
                    UseProxy = true,
                    Proxy = HttpClient.DefaultProxy,
                    DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                });
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                _youtubeClient = new YoutubeClient(_httpClient);

                CheckFirstRun();
                Logger.Log("CheckFirstRun done");

                SetInitialTargetLangSelections();
                LoadFromAppData();

                BaseUrlTextBox.TextChanged += (_, _) => AutoSaveConfig();
                ModelTextBox.TextChanged += (_, _) => AutoSaveConfig();
                ApiKeyTextBox.PasswordChanged += (_, _) => AutoSaveConfig();

                LoadLayoutPreference();
                Logger.Log($"Layout preference: {_currentLayout}");

                ApplyLayoutMode(_currentLayout);
                Logger.Log("ApplyLayoutMode done");

                ApplyLanguage();
                Logger.Log("ApplyLanguage done");

                // Delay hint popup until window is fully loaded
                Loaded += (_, _) =>
                {
                    Logger.Log("Window Loaded event fired");
                    CheckLayoutHintFirstRun();
                    Logger.Log("CheckLayoutHintFirstRun done");
                };

                Logger.Log("MainWindow constructor end");
            }
            catch (Exception ex)
            {
                Logger.LogError("MainWindow constructor exception", ex);
                MessageBox.Show(ex.ToString(), "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void AutoSaveConfig()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string myAppFolder = System.IO.Path.Combine(appDataPath, "SimpVid Gist");
                if (!Directory.Exists(myAppFolder))
                    Directory.CreateDirectory(myAppFolder);
                string filePath = System.IO.Path.Combine(myAppFolder, "userdata.txt");
                File.WriteAllText(filePath, BaseUrlTextBox.Text + "\n" + ModelTextBox.Text + "\n" + ApiKeyTextBox.Password, Encoding.UTF8);
                MessageBox.Show($"Successfully saved.\nAt: {filePath}\nThe next time you open SimpVid Gist, your data will be automatically read.", "", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show($"Save Failed.\nAn unknown error occured.", "", MessageBoxButton.OK, MessageBoxImage.Error);

            }
        }

        private void PopulateExportFormats()
        {
            bool zh = Localization.IsChinese;
            int prevIdx = ExportFormatComboBox.SelectedIndex;
            ExportFormatComboBox.ItemsSource = new List<ExportFormatItem>
            {
                new() { Display = zh ? "TXT（纯文本）" : "TXT (text only)", Value = "txt" },
                new() { Display = zh ? "SRT（带时间轴）" : "SRT (with timestamps)", Value = "srt" },
            };
            ExportFormatComboBox.DisplayMemberPath = "Display";
            ExportFormatComboBox.SelectedIndex = prevIdx >= 0 ? prevIdx : 0;
        }

        private void PopulateSummaryLengths()
        {
            bool zh = Localization.IsChinese;
            int prevIdx = SummaryLengthComboBox.SelectedIndex;
            SummaryLengthComboBox.ItemsSource = new List<SummaryLengthItem>
            {
                new() { Display = zh ? "极简（100字）" : "Minimal (100 chars)", WordCount = 100 },
                new() { Display = zh ? "简短（300字）" : "Short (300 chars)", WordCount = 300 },
                new() { Display = zh ? "中等（500字）" : "Medium (500 chars)", WordCount = 500 },
                new() { Display = zh ? "详细（1000字）" : "Long (1000 chars)", WordCount = 1000 },
                new() { Display = zh ? "自定义" : "Custom", WordCount = null },
            };
            SummaryLengthComboBox.DisplayMemberPath = "Display";
            SummaryLengthComboBox.SelectedIndex = prevIdx >= 0 ? prevIdx : 2;
        }

        private void UpdateLanguageCodeItems(bool zh)
        {
            LangCodeEn.Content = zh ? "英语 (en)" : "English (en)";
            LangCodeZh.Content = zh ? "中文 (zh)" : "Chinese (zh)";
            LangCodeJa.Content = zh ? "日语 (ja)" : "Japanese (ja)";
            LangCodeKo.Content = zh ? "韩语 (ko)" : "Korean (ko)";
            LangCodeEs.Content = zh ? "西班牙语 (es)" : "Spanish (es)";
            LangCodeFr.Content = zh ? "法语 (fr)" : "French (fr)";
            LangCodeRu.Content = zh ? "俄语 (ru)" : "Russian (ru)";
        }

        private void UpdateModeItems(bool zh)
        {
            ModeItemSummary.Content = zh ? "AI总结" : "AI Summary";
            ModeItemTranslation.Content = zh ? "AI翻译" : "AI Translation";
            ModeItemKnowledge.Content = zh ? "AI知识图表" : "AI Knowledge Graph";
        }

        private void UpdateTargetLangCustomItems(bool zh)
        {
            string custom = zh ? "自定义" : "Custom";
            SummaryTargetCustom.Content = custom;
            TranslationTargetCustom.Content = custom;
            KnowledgeTargetCustom.Content = custom;
        }

        private void SetInitialTargetLangSelections()
        {
            int idx = Localization.IsChinese ? 1 : 0;
            SummaryTargetLangComboBox.SelectedIndex = idx;
            TranslationTargetLangComboBox.SelectedIndex = idx;
            KnowledgeTargetLangComboBox.SelectedIndex = idx;
        }

        private void ApplyLanguage()
        {
            bool zh = Localization.IsChinese;

            Title = "SimpVid Gist";
            LangToggleButton.Content = zh ? "EN" : "中";
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

            SummarizationGroupBox.Header = zh ? "AI处理" : "AI Processing";
            TranscriptGroupBox.Header = zh ? "字幕" : "Transcript";
            BaseUrlLabel.Text = zh ? "AI接口地址" : "AI Base URL (API Endpoint)";
            BaseUrlPlaceholder.Text = zh ? "例: https://api.openai.com/v1" : "e.g. https://api.openai.com/v1";
            BaseUrlPlaceholder.Visibility = string.IsNullOrEmpty(BaseUrlTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ModelNameLabel.Text = zh ? "模型名称" : "Model Name";
            ApiKeyLabel.Text = zh ? "API密钥" : "AI API key";
            SaveButton.Content = zh ? "保存AI配置" : "Save AI Base URL and Model Name";
            ModeLabel.Text = zh ? "模式" : "Mode";
            SummaryTargetLangLabel.Text = zh ? "目标总结语言" : "Target Summary Language";
            SummaryLengthLabel.Text = zh ? "总结字数" : "Summary Length";
            TranslationTargetLangLabel.Text = zh ? "目标翻译语言" : "Target Translation Language";
            KnowledgeTargetLangLabel.Text = zh ? "目标语言" : "Target Language";
            KnowledgePreviewLabel.Text = zh ? "预览" : "Preview";
            ExportSvgButton.Content = zh ? "导出SVG" : "Export SVG";
            ExportPngButton.Content = zh ? "导出PNG" : "Export PNG";
            SummarizeButton.Content = _currentMode switch
            {
                AiMode.Translation => zh ? "翻译字幕" : "Translate Transcript",
                AiMode.KnowledgeGraph => zh ? "生成知识图表" : "Generate Knowledge Graph",
                _ => zh ? "总结字幕" : "Summarize Transcript"
            };
            ExportResultButton.Content = zh ? "导出 TXT" : "Export TXT";
            SummaryLabel.Text = zh ? "结果" : "Result";

            int langIdx = LanguageCodeComboBox.SelectedIndex;
            int expIdx = ExportFormatComboBox.SelectedIndex;
            int sumIdx = SummaryLengthComboBox.SelectedIndex;
            int modeIdx = ModeComboBox.SelectedIndex;
            int sumTargetIdx = SummaryTargetLangComboBox.SelectedIndex;
            int transTargetIdx = TranslationTargetLangComboBox.SelectedIndex;
            int knowTargetIdx = KnowledgeTargetLangComboBox.SelectedIndex;

            UpdateLanguageCodeItems(zh);
            PopulateExportFormats();
            UpdateModeItems(zh);
            UpdateTargetLangCustomItems(zh);
            PopulateSummaryLengths();

            if (langIdx >= 0 && langIdx < LanguageCodeComboBox.Items.Count)
                LanguageCodeComboBox.SelectedIndex = langIdx;

            if (expIdx >= 0 && expIdx < ExportFormatComboBox.Items.Count)
                ExportFormatComboBox.SelectedIndex = expIdx;
            if (modeIdx >= 0 && modeIdx < ModeComboBox.Items.Count)
                ModeComboBox.SelectedIndex = modeIdx;
            if (sumTargetIdx >= 0 && sumTargetIdx < SummaryTargetLangComboBox.Items.Count)
                SummaryTargetLangComboBox.SelectedIndex = sumTargetIdx;
            if (transTargetIdx >= 0 && transTargetIdx < TranslationTargetLangComboBox.Items.Count)
                TranslationTargetLangComboBox.SelectedIndex = transTargetIdx;
            if (knowTargetIdx >= 0 && knowTargetIdx < KnowledgeTargetLangComboBox.Items.Count)
                KnowledgeTargetLangComboBox.SelectedIndex = knowTargetIdx;
            if (sumIdx >= 0 && sumIdx < SummaryLengthComboBox.Items.Count)
                SummaryLengthComboBox.SelectedIndex = sumIdx;

            bool isCustom = (SummaryLengthComboBox.SelectedItem as SummaryLengthItem)?.WordCount == null;
            CustomLengthTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            // Localize layout toggle control + hint popup
            UpdateLayoutToggleButtonVisual();
            if (LayoutHintPopup.IsOpen)
            {
                LayoutHintTitle.Text = zh ? "布局切换" : "Switch Layout";
                LayoutHintBody.Text = zh
                    ? "点此按钮可在「上下滚动」与「左右分栏」两种界面布局间切换。"
                    : "Click this button to switch between Scroll and Split layouts.";
            }
        }

        private void LangToggleButton_Click(object sender, RoutedEventArgs e)
        {
            Localization.IsChinese = !Localization.IsChinese;
            ApplyLanguage();
        }

        private void SummaryLengthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isCustom = (SummaryLengthComboBox.SelectedItem as SummaryLengthItem)?.WordCount == null;
            CustomLengthTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ModeComboBox.SelectedItem as ComboBoxItem;
            _currentMode = item?.Tag?.ToString() switch
            {
                "translation" => AiMode.Translation,
                "knowledge" => AiMode.KnowledgeGraph,
                _ => AiMode.Summary
            };
            bool zh = Localization.IsChinese;
            SummaryPanel.Visibility = _currentMode == AiMode.Summary ? Visibility.Visible : Visibility.Collapsed;
            TranslationPanel.Visibility = _currentMode == AiMode.Translation ? Visibility.Visible : Visibility.Collapsed;
            KnowledgeGraphPanel.Visibility = _currentMode == AiMode.KnowledgeGraph ? Visibility.Visible : Visibility.Collapsed;
            SummarizeButton.Content = _currentMode switch
            {
                AiMode.Translation => zh ? "翻译字幕" : "Translate Transcript",
                AiMode.KnowledgeGraph => zh ? "生成知识图表" : "Generate Knowledge Graph",
                _ => zh ? "总结字幕" : "Summarize Transcript"
            };
        }

        private void SummaryTargetLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateCustomLangVisibility(SummaryTargetLangComboBox, SummaryCustomLangTextBox);

        private void TranslationTargetLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateCustomLangVisibility(TranslationTargetLangComboBox, TranslationCustomLangTextBox);

        private void KnowledgeTargetLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateCustomLangVisibility(KnowledgeTargetLangComboBox, KnowledgeCustomLangTextBox);

        private static void UpdateCustomLangVisibility(ComboBox combo, TextBox customTextBox)
        {
            bool isCustom = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "custom";
            customTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        private string GetTargetLanguage(ComboBox comboBox, TextBox customTextBox)
        {
            var item = comboBox.SelectedItem as ComboBoxItem;
            if (item?.Tag?.ToString() == "custom")
                return customTextBox.Text.Trim();
            return item?.Tag?.ToString() ?? "en";
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
            => UpdateExpandableDisplay(TranscriptTextBox, ShowMoreButton, _fullTranscript, _isExpanded);

        private void UpdateSummaryDisplay()
            => UpdateExpandableDisplay(SummaryTextBox, SummaryShowMoreButton, _fullSummary, _isSummaryExpanded);

        private static void UpdateExpandableDisplay(TextBox textBox, Button toggleButton, string fullContent, bool isExpanded)
        {
            if (string.IsNullOrEmpty(fullContent))
            {
                textBox.Text = "";
                toggleButton.Visibility = Visibility.Collapsed;
                return;
            }

            var lines = fullContent.Split('\n');

            if (lines.Length <= MaxPreviewLines)
            {
                textBox.Text = fullContent;
                toggleButton.Visibility = Visibility.Collapsed;
                return;
            }

            toggleButton.Visibility = Visibility.Visible;
            if (isExpanded)
            {
                textBox.Text = fullContent;
                toggleButton.Content = "▲";
            }
            else
            {
                textBox.Text = string.Join("\n", lines.Take(MaxPreviewLines)) + "\n...";
                toggleButton.Content = "▼";
            }
        }

        private void ShowMoreButton_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;
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
            TranscriptTextBox.Text = zh ? "正在获取字幕..." : "Fetching transcript tracks...";
            try
            {
                string targetLangCode = (LanguageCodeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
                bool usedFallback = false;
                List<ClosedCaption> captions = null;

                try
                {
                    var trackManifest = await _youtubeClient.Videos.ClosedCaptions.GetManifestAsync(videoInput);

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
                        captions = closedCaptionTrack.Captions.ToList();
                    }
                }
                catch (Exception ex) when (ex.GetType().Name.Contains("VideoUnavailable") || ex.GetType().Name.Contains("ClosedCaptionsUnavailable") || ex.Message.Contains("not available"))
                {
                    TranscriptTextBox.Text = zh ? "主接口失败，正在尝试备用方案..." : "Primary API failed, trying fallback...";

                    // Try youtubetranscript.com first
                    captions = await GetTranscriptFallbackAsync(videoInput, targetLangCode);

                    // If that also fails, try direct connection (no proxy) as last resort
                    if (captions == null || captions.Count == 0)
                    {
                        TranscriptTextBox.Text = zh ? "正在尝试直连..." : "Trying direct connection...";
                        using var noProxyClient = new HttpClient(new SocketsHttpHandler
                        {
                            UseProxy = false,
                            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
                        });
                        noProxyClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                        noProxyClient.Timeout = TimeSpan.FromSeconds(15);
                        try
                        {
                            var fallbackJson = await noProxyClient.GetStringAsync($"https://youtubetranscript.com/api?vid={ExtractYouTubeVideoId(videoInput)}&lang={targetLangCode}");
                            captions = ParseTranscriptJson(fallbackJson);
                        }
                        catch { /* final fallback failed */ }
                    }

                    usedFallback = true;
                }

                if (captions != null && captions.Count > 0)
                {
                    _captions = captions;
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

                    if (usedFallback)
                    {
                        string fbMsg = zh ? "（已使用备用方案获取字幕）" : "(Transcript fetched via fallback)";
                        TranscriptTextBox.Text = transcriptBuilder.Length > 0
                            ? transcriptBuilder.ToString() + "\n" + fbMsg
                            : fbMsg;
                    }
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
                MessageBox.Show(zh ? "请先提取有效的字幕。" : "Please fetch a valid transcript first.", zh ? "字幕为空" : "Transcript Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            try
            {
                SummaryTextBox.Text = "";
                SummaryShowMoreButton.Visibility = Visibility.Collapsed;
                _fullSummary = "";

                string result;
                switch (_currentMode)
                {
                    case AiMode.Summary:
                    {
                        int wordLimit = GetWordCountLimit();
                        string targetLang = GetTargetLanguage(SummaryTargetLangComboBox, SummaryCustomLangTextBox);
                        if (string.IsNullOrWhiteSpace(targetLang)) targetLang = zh ? "zh" : "en";
                        string prompt = BuildSummaryPrompt(wordLimit, targetLang);
                        result = await CallAiAsync(apiKey, transcript, apiUrl, modelName, prompt,
                            onChunk: chunk =>
                            {
                                _fullSummary += chunk;
                                Dispatcher.Invoke(() => SummaryTextBox.Text = _fullSummary);
                            });
                        break;
                    }
                    case AiMode.Translation:
                    {
                        string targetLang = GetTargetLanguage(TranslationTargetLangComboBox, TranslationCustomLangTextBox);
                        if (string.IsNullOrWhiteSpace(targetLang)) targetLang = zh ? "zh" : "en";
                        string prompt = BuildTranslationPrompt(targetLang);
                        result = await CallAiAsync(apiKey, transcript, apiUrl, modelName, prompt,
                            onChunk: chunk =>
                            {
                                _fullSummary += chunk;
                                Dispatcher.Invoke(() => SummaryTextBox.Text = _fullSummary);
                            });
                        break;
                    }
                    case AiMode.KnowledgeGraph:
                    {
                        string targetLang = GetTargetLanguage(KnowledgeTargetLangComboBox, KnowledgeCustomLangTextBox);
                        if (string.IsNullOrWhiteSpace(targetLang)) targetLang = zh ? "zh" : "en";
                        string prompt = BuildKnowledgeGraphPrompt(targetLang);
                        result = await CallAiAsync(apiKey, transcript, apiUrl, modelName, prompt,
                            onChunk: chunk => _fullSummary += chunk);
                        _lastMermaidCode = result;
                        _isSummaryExpanded = false;
                        UpdateSummaryDisplay();
                        await RenderMermaidAsync(result);
                        return;
                    }
                    default:
                        result = "";
                        break;
                }
                _fullSummary = result;
                _isSummaryExpanded = false;
                UpdateSummaryDisplay();
            }
            catch (Exception ex)
            {
                _fullSummary = zh ? $"AI错误: {ex.Message}" : $"AI Error: {ex.Message}";
                _isSummaryExpanded = true;
                UpdateSummaryDisplay();
            }
            finally
            {
                SummarizeButton.IsEnabled = true;
                ExtractButton.IsEnabled = true;
            }
        }

        private string BuildSummaryPrompt(int wordLimit, string targetLang)
        {
            bool zh = Localization.IsChinese;
            return zh
                ? $"你是一个专业的AI总结助手。请将以下YouTube字幕总结为清晰、有条理的段落。可以使用要点。\n总结语言：{targetLang}\n\n【硬性约束】总结必须精确输出{wordLimit}个字，不得多不得少！你必须精确计数，输出字数必须严格等于{wordLimit}。如果字数不符，请调整内容直到完全匹配。三项原则：1)输出精确{wordLimit}字 2)保留核心信息 3)语言简洁精炼。\n\n直接输出总结内容，无需任何额外说明。"
                : $"You are an expert AI summarizer. Summarize the following YouTube transcript into clear, structured paragraphs. You may use bullet points.\nOutput language: {targetLang}\n\n【STRICT CONSTRAINT】The summary must be EXACTLY {wordLimit} characters — no more, no less! You MUST count precisely and output exactly {wordLimit} characters. If the count doesn't match, adjust the content until it does. Three rules: 1) Output exactly {wordLimit} characters 2) Keep all key information 3) Be concise.\n\nOutput the summary directly without any additional notes.";
        }

        private string BuildTranslationPrompt(string targetLang)
        {
            bool zh = Localization.IsChinese;
            return zh
                ? $"你是一个专业翻译。请将以下YouTube字幕翻译成{targetLang}。要求：1)结合上下文进行精确翻译 2)语言地道自然，符合{targetLang}表达习惯 3)保持原意完整 4)保留关键术语和专有名词。\n\n直接输出翻译结果，无需任何额外说明。"
                : $"You are a professional translator. Translate the following YouTube transcript into {targetLang}. Requirements: 1) Use context for accurate translation 2) Natural and idiomatic expression 3) Preserve the complete meaning 4) Keep key terms and proper nouns. \n\nOutput the translation directly without any additional notes.";
        }

        private string BuildKnowledgeGraphPrompt(string targetLang)
        {
            return $"You are an expert at creating mind maps. Analyze the following transcript and create a comprehensive mind map in Mermaid.js format. Use the 'mindmap' diagram type.\nOutput language: {targetLang}\n\nRequirements:\n1. Start with 'mindmap' as the root\n2. Capture the MAIN topic as the central root node\n3. Break down into logical subtopics, sub-subtopics, and key details\n4. Use clear, concise labels for each node\n5. Organize hierarchically like a mind map — do NOT miss any important information or detail\n6. Be accurate and precise\n7. Output language must be {targetLang}\n\nCRITICAL: Output ONLY valid Mermaid.js mindmap code. No explanations, no markdown fences, no extra text. Start directly with 'mindmap'.";
        }

        private async Task<string> CallAiAsync(string apiKey, string transcriptContent, string apiUrl, string modelName, string systemPrompt, Action<string> onChunk = null)
        {
            bool zh = Localization.IsChinese;

            if (!apiUrl.EndsWith("/chat/completions"))
                apiUrl = apiUrl.TrimEnd('/') + "/chat/completions";

            var requestBody = new JsonObject
            {
                ["model"] = modelName,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                    new JsonObject { ["role"] = "user", ["content"] = transcriptContent }
                },
                ["temperature"] = 0.5,
                ["stream"] = true
            };

            string jsonPayload = requestBody.ToJsonString();

            using (var request = new HttpRequestMessage(HttpMethod.Post, apiUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    string errorDetails = await response.Content.ReadAsStringAsync();
                    throw new Exception($"({response.StatusCode}) Details: {errorDetails}");
                }

                var fullBuilder = new StringBuilder();
                using var streamResponse = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(streamResponse);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("data: "))
                    {
                        var data = line.Substring(6);
                        if (data.Trim() == "[DONE]") break;

                        try
                        {
                            using var doc = JsonDocument.Parse(data);
                            var choices = doc.RootElement.GetProperty("choices");
                            if (choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                            {
                                var delta = choices[0].GetProperty("delta");
                                if (delta.TryGetProperty("content", out var contentProp))
                                {
                                    string chunk = contentProp.GetString() ?? "";
                                    fullBuilder.Append(chunk);
                                    onChunk?.Invoke(chunk);
                                }
                            }
                        }
                        catch { /* skip malformed chunk */ }
                    }
                }

                string fullText = fullBuilder.ToString().Trim();
                return string.IsNullOrEmpty(fullText)
                    ? (zh ? "AI返回了空响应。" : "AI returned an empty response.")
                    : fullText;
            }
        }

        private void SummaryShowMoreButton_Click(object sender, RoutedEventArgs e)
        {
            _isSummaryExpanded = !_isSummaryExpanded;
            UpdateSummaryDisplay();
        }

        private void ExportResultButton_Click(object sender, RoutedEventArgs e)
        {
            bool zh = Localization.IsChinese;
            string text = SummaryTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith(zh ? "AI错误" : "AI Error"))
            {
                MessageBox.Show(zh ? "没有可导出的内容。" : "Nothing to export.", "", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "AI_Result_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, _fullSummary, Encoding.UTF8);
                    MessageBox.Show(zh ? "文件已保存。" : "File saved.", "", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(zh ? $"导出失败: {ex.Message}" : $"Export failed: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static string StripCodeFences(string mermaidCode)
        {
            string code = mermaidCode.Trim();
            if (code.StartsWith("```"))
            {
                int start = code.IndexOf('\n');
                if (start > 0) code = code[(start + 1)..];
                int end = code.LastIndexOf("```");
                if (end >= 0) code = code[..end];
                code = code.Trim();
            }
            return code;
        }

        private async Task RenderMermaidAsync(string mermaidCode)
        {
            string code = StripCodeFences(mermaidCode);
            bool zh = Localization.IsChinese;

            string html;
            if (string.IsNullOrWhiteSpace(code))
            {
                html = $@"<!DOCTYPE html><html><head><meta charset=""utf-8""/></head><body><p style=""color:red;padding:20px;"">{System.Net.WebUtility.HtmlEncode(zh ? "AI 返回了空的思维导图代码。" : "AI returned empty mindmap code.")}</p></body></html>";
            }
            else
            {
                try
                {
                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
                    string url = $"https://mermaid.ink/svg/{encoded}";
                    byte[] svgData = await _httpClient.GetByteArrayAsync(url);
                    string svgText = Encoding.UTF8.GetString(svgData);
                    html = $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""/>
<meta http-equiv=""X-UA-Compatible"" content=""IE=edge""/>
<style>
*{{margin:0;padding:0}}
body{{overflow:hidden;width:100vw;height:100vh;background:#fff}}
#graph{{width:100%;min-height:100vh;padding:20px;box-sizing:border-box;text-align:center}}
#graph svg{{max-width:100%;height:auto}}
</style>
</head><body>
<div id=""graph"">
{svgText}
</div>
<script>
var scale=1,tx=0,ty=0;
var g=document.getElementById('graph');
document.addEventListener('wheel',function(e){{e.preventDefault();var d=e.deltaY>0?.9:1.1;scale*=d;scale=Math.max(.1,Math.min(5,scale));g.style.transform='scale('+scale+') translate('+(tx/scale)+'px,'+(ty/scale)+'px)';g.style.transformOrigin='0 0';}},false);
var drag=0,dx,dy;
document.addEventListener('mousedown',function(e){{drag=1;dx=e.clientX-tx;dy=e.clientY-ty;}});
document.addEventListener('mousemove',function(e){{if(drag){{tx=e.clientX-dx;ty=e.clientY-dy;g.style.transform='scale('+scale+') translate('+(tx/scale)+'px,'+(ty/scale)+'px)';g.style.transformOrigin='0 0';}}}});
document.addEventListener('mouseup',function(){{drag=0;}});
</script></body></html>";
                }
                catch (Exception ex)
                {
                    html = $@"<!DOCTYPE html><html><head><meta charset=""utf-8""/></head><body><p style=""color:red;padding:20px;"">{System.Net.WebUtility.HtmlEncode(ex.Message)}</p></body></html>";
                }
            }

            string tempFile = Path.Combine(Path.GetTempPath(), "mermaid_preview.html");
            await File.WriteAllTextAsync(tempFile, html, Encoding.UTF8);
            MermaidBrowser.Navigate(new Uri(tempFile));
        }

        private async void ExportSvgButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportMermaidAsync("svg", "SVG Files (*.svg)|*.svg|All Files (*.*)|*.*", "svg",
                zh => zh ? "SVG已导出。" : "SVG exported.");
        }

        private async void ExportPngButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportMermaidAsync("img", "PNG Files (*.png)|*.png|All Files (*.*)|*.*", "png",
                zh => zh ? "PNG已导出。" : "PNG exported.");
        }

        private async Task ExportMermaidAsync(string endpoint, string filter, string ext, Func<bool, string> successMsg)
        {
            bool zh = Localization.IsChinese;
            string code = StripCodeFences(_lastMermaidCode);
            if (string.IsNullOrEmpty(code))
            {
                MessageBox.Show(zh ? "请先生成知识图表。" : "Please generate a knowledge graph first.", "", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = ext,
                FileName = "KnowledgeGraph_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
                string url = $"https://mermaid.ink/{endpoint}/{encoded}";
                byte[] data = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(dialog.FileName, data);
                MessageBox.Show(successMsg(zh), "", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (HttpRequestException ex) when (ex.StatusCode != null)
            {
                MessageBox.Show(zh ? $"导出失败 (HTTP {(int)ex.StatusCode}): {ex.Message}" : $"Export failed (HTTP {(int)ex.StatusCode}): {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(zh ? $"导出失败: {ex.Message}" : $"Export failed: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (e.ClickCount >= 2)
                {
                    ToggleMaximize();
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void ToggleMaximize()
        {
            if (_isMaximized)
            {
                _isMaximized = false;
                WindowState = WindowState.Normal;
                Left = _normalBounds.Left;
                Top = _normalBounds.Top;
                Width = _normalBounds.Width;
                Height = _normalBounds.Height;
            }
            else
            {
                ApplyMaximizeBounds();
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized && !_isMaximized)
            {
                ApplyMaximizeBounds();
            }
            else if (WindowState == WindowState.Normal && _isMaximized)
            {
                _isMaximized = false;
            }
        }

        private void ApplyMaximizeBounds()
        {
            _normalBounds = new Rect(Left, Top, Width, Height);
            _isMaximized = true;
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AutoSaveConfig();
        }

        private void Button_Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private bool ShowRetryDialog(string langCode)
        {
            bool zh = Localization.IsChinese;
            var dialog = new RetryDialog(langCode, zh) { Owner = this };
            dialog.ShowDialog();
            return dialog.IsAutoRetry;
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

            var dialog = new LanguagePickerDialog();
            dialog.ShowDialog();
            string chosenLang = dialog.SelectedLanguage;

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
                        BaseUrlTextBox.Text = lines[0];
                    if (lines.Length >= 2)
                        ModelTextBox.Text = lines[1];
                    if (lines.Length >= 3)
                        ApiKeyTextBox.Password = lines[2];
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

        private void BaseUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            BaseUrlPlaceholder.Visibility = string.IsNullOrEmpty(BaseUrlTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string ExtractYouTubeVideoId(string input)
        {
            input = input.Trim();
            if (string.IsNullOrWhiteSpace(input)) return null;

            // Handle various URL formats
            if (input.Contains("youtube.com/watch") && input.Contains("v="))
            {
                int idx = input.IndexOf("v=") + 2;
                int end = input.IndexOf('&', idx);
                if (end < 0) end = input.Length;
                return input.Substring(idx, end - idx);
            }

            if (input.Contains("youtu.be/"))
            {
                int idx = input.IndexOf("youtu.be/") + 9;
                int end = input.IndexOf('?', idx);
                if (end < 0) end = input.Length;
                string id = input.Substring(idx, end - idx);
                int slash = id.IndexOf('/');
                if (slash >= 0) id = id.Substring(0, slash);
                return id;
            }

            if (input.Contains("youtube.com/shorts/"))
            {
                int idx = input.IndexOf("shorts/") + 7;
                int end = input.IndexOf('?', idx);
                if (end < 0) end = input.Length;
                string id = input.Substring(idx, end - idx);
                int slash = id.IndexOf('/');
                if (slash >= 0) id = id.Substring(0, slash);
                return id;
            }

            // Assume it's already a bare video ID
            return input;
        }

        private async Task<List<ClosedCaption>> GetTranscriptFallbackAsync(string videoInput, string langCode)
        {
            bool zh = Localization.IsChinese;
            string videoId = ExtractYouTubeVideoId(videoInput);

            if (string.IsNullOrWhiteSpace(videoId) || videoId.Length < 10)
                return new List<ClosedCaption>();

            string fallbackUrl = $"https://youtubetranscript.com/api?vid={videoId}&lang={langCode}";
            TranscriptTextBox.Text = zh ? "正在通过备用接口获取字幕..." : "Fetching transcript via fallback API...";

            var response = await _httpClient.GetAsync(fallbackUrl);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            return ParseTranscriptJson(json);
        }

        private static List<ClosedCaption> ParseTranscriptJson(string json)
        {
            var captions = new List<ClosedCaption>();
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string text = item.GetProperty("text").GetString() ?? "";
                double start = item.GetProperty("start").GetDouble();
                double duration = item.GetProperty("duration").GetDouble();
                captions.Add(new ClosedCaption(
                    text,
                    TimeSpan.FromSeconds(start),
                    TimeSpan.FromSeconds(duration),
                    Array.Empty<ClosedCaptionPart>()));
            }
            return captions;
        }

        // ===== Layout mode management =====

        private void ApplyLayoutMode(LayoutMode mode)
        {
            try
            {
                Logger.Log($"ApplyLayoutMode: mode={mode}");

                // WebBrowser (ActiveX host) crashes when reparented.
                // Detach MermaidBrowser from its host Border before reparenting AiSectionPanel,
                // then reattach after reparenting is complete.
                Border? mermaidHost = null;
                try
                {
                    if (MermaidBrowser.Parent is Border mb)
                    {
                        mermaidHost = mb;
                        mb.Child = null;
                        Logger.Log("Detached MermaidBrowser from host Border before reparent");
                    }
                }
                catch (Exception ex) { Logger.LogError("MermaidBrowser detach", ex); }

                DetachFromParent(SubtitleSectionPanel);
                DetachFromParent(AiSectionPanel);
                Logger.Log("Detached both panels from parents");

                if (mode == LayoutMode.Split)
                {
                    SubtitleSectionPanel.Margin = new Thickness(16);
                    AiSectionPanel.Margin = new Thickness(16);
                    // In Split mode, panels live inside their own ScrollViewer in each grid column
                    SubtitleScrollViewer.Content = SubtitleSectionPanel;
                    AiScrollViewer.Content = AiSectionPanel;
                    SplitLayoutGrid.Visibility = Visibility.Visible;
                    ScrollLayoutViewer.Visibility = Visibility.Collapsed;
                    Logger.Log("Split layout applied");
                }
                else
                {
                    SubtitleSectionPanel.Margin = new Thickness(0);
                    AiSectionPanel.Margin = new Thickness(0);
                    ScrollSectionSubtitle.Child = SubtitleSectionPanel;
                    ScrollSectionAi.Child = AiSectionPanel;
                    ScrollLayoutViewer.Visibility = Visibility.Visible;
                    SplitLayoutGrid.Visibility = Visibility.Collapsed;
                    ScrollLayoutViewer.ScrollToTop();
                    Logger.Log("Scroll layout applied");
                }

                // Reattach WebBrowser
                if (mermaidHost != null)
                {
                    try
                    {
                        mermaidHost.Child = MermaidBrowser;
                        Logger.Log("Reattached MermaidBrowser to host Border");
                    }
                    catch (Exception ex) { Logger.LogError("MermaidBrowser reattach", ex); }
                }

                _currentLayout = mode;
                UpdateLayoutToggleButtonVisual();

                // Re-render Mermaid diagram if needed (WebBrowser may have lost state)
                if (!string.IsNullOrEmpty(_lastMermaidCode) && _currentMode == AiMode.KnowledgeGraph)
                {
                    Logger.Log("Re-rendering Mermaid diagram");
                    _ = RenderMermaidAsync(_lastMermaidCode);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ApplyLayoutMode exception", ex);
                // Fallback: ensure at least split layout is visible so app doesn't show blank
                try
                {
                    SplitLayoutGrid.Visibility = Visibility.Visible;
                    ScrollLayoutViewer.Visibility = Visibility.Collapsed;
                    _currentLayout = LayoutMode.Split;
                }
                catch { }
            }
        }

        private static void DetachFromParent(FrameworkElement element)
        {
            if (element.Parent == null) return;
            switch (element.Parent)
            {
                case Panel p:
                    p.Children.Remove(element);
                    break;
                case Border b:
                    b.Child = null;
                    break;
                case ContentControl c:
                    c.Content = null;
                    break;
            }
        }

        private void LayoutToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var next = _currentLayout == LayoutMode.Split ? LayoutMode.Scroll : LayoutMode.Split;
            ApplyLayoutMode(next);
            SaveLayoutPreference();
            LayoutHintPopup.IsOpen = false;
        }

        private void UpdateLayoutToggleButtonVisual()
        {
            bool zh = Localization.IsChinese;
            LayoutToggleButton.ToolTip = _currentLayout == LayoutMode.Split
                ? (zh ? "当前：分栏模式 · 点击切换为滚动模式" : "Current: Split · Click to switch to Scroll")
                : (zh ? "当前：滚动模式 · 点击切换为分栏模式" : "Current: Scroll · Click to switch to Split");
        }

        private void ScrollLayout_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_currentLayout != LayoutMode.Scroll) return;
            if (_isAnimatingScroll) { e.Handled = true; return; }

            double offset = ScrollLayoutViewer.VerticalOffset;
            double section0H = ScrollSectionSubtitle.ActualHeight;
            if (section0H <= 0) return;

            bool atTopOfSection0 = offset <= 5;

            if (e.Delta < 0) // wheel down
            {
                if (atTopOfSection0)
                {
                    e.Handled = true;
                    AnimateScrollTo(section0H);
                }
                // else: default free scroll within section 1
            }
            else // wheel up
            {
                // At or near section 1 top (within 100px) → snap to section 0
                if (offset <= section0H + 100 && offset >= section0H - 5)
                {
                    e.Handled = true;
                    AnimateScrollTo(0);
                }
                else if (atTopOfSection0)
                {
                    e.Handled = true; // no-op, already at top
                }
                // else: default free scroll within section 1
            }
        }

        private void AnimateScrollTo(double targetOffset)
        {
            _isAnimatingScroll = true;
            var anim = new DoubleAnimation
            {
                From = ScrollLayoutViewer.VerticalOffset,
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(550),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (s, _) =>
            {
                _isAnimatingScroll = false;
            };
            ScrollViewerAnimationBehavior.SetVerticalOffset(ScrollLayoutViewer, ScrollLayoutViewer.VerticalOffset);
            ScrollLayoutViewer.BeginAnimation(ScrollViewerAnimationBehavior.VerticalOffsetProperty, anim);
        }

        private void LoadLayoutPreference()
        {
            try
            {
                if (File.Exists(LayoutModeFile))
                {
                    string v = File.ReadAllText(LayoutModeFile, Encoding.UTF8).Trim().ToLower();
                    _currentLayout = v == "split" ? LayoutMode.Split : LayoutMode.Scroll;
                }
                else
                {
                    _currentLayout = LayoutMode.Scroll;
                }
            }
            catch { _currentLayout = LayoutMode.Scroll; }
        }

        private void SaveLayoutPreference()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(LayoutModeFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LayoutModeFile,
                    _currentLayout == LayoutMode.Split ? "split" : "scroll", Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        private void CheckLayoutHintFirstRun()
        {
            try
            {
                if (File.Exists(LayoutHintFile)) return;
                bool zh = Localization.IsChinese;
                LayoutHintTitle.Text = zh ? "布局切换" : "Switch Layout";
                LayoutHintBody.Text = zh
                    ? "点此按钮可在「上下滚动」与「左右分栏」两种界面布局间切换。"
                    : "Click this button to switch between Scroll and Split layouts.";
                LayoutHintPopup.IsOpen = true;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                timer.Tick += (s, _) => { LayoutHintPopup.IsOpen = false; timer.Stop(); };
                timer.Start();
                string dir = System.IO.Path.GetDirectoryName(LayoutHintFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(LayoutHintFile, "1", Encoding.UTF8);
            }
            catch { /* ignore */ }
        }
    }

    public static class ScrollViewerAnimationBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset", typeof(double), typeof(ScrollViewerAnimationBehavior),
                new PropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static double GetVerticalOffset(DependencyObject obj) => (double)obj.GetValue(VerticalOffsetProperty);
        public static void SetVerticalOffset(DependencyObject obj, double value) => obj.SetValue(VerticalOffsetProperty, value);

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
                sv.ScrollToVerticalOffset((double)e.NewValue);
        }
    }
}
