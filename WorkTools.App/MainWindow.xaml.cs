using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WorkTools.Core;
using WinRT.Interop;

namespace WorkTools.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _debounceTimer;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly Dictionary<string, string> _sectionFullContent = new();

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "WorkTools";
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);

        _debounceTimer = DispatcherQueue.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(500);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += async (_, _) => await UpdatePreviewAsync();

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(3);
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => StatusBar.IsOpen = false;

        TemplateScroll.SizeChanged += ScrollViewer_SizeChanged;
        TagsScroll.SizeChanged += ScrollViewer_SizeChanged;
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        QueuePreviewRefresh();
        UpdateButtonStates();
    }

    private void InputOption_Changed(object sender, RoutedEventArgs e)
    {
        QueuePreviewRefresh();
    }

    private void QueuePreviewRefresh()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private string[] GetEffectiveTagNames(string tagsText)
    {
        return TemplateExpander.ParseTagNames(tagsText, SortTagsCheckBox.IsChecked == true);
    }

    private string GetEffectiveTagsText(string tagsText)
    {
        return string.Join(Environment.NewLine, GetEffectiveTagNames(tagsText));
    }

    private void UpdateButtonStates()
    {
        bool hasTemplate = !string.IsNullOrWhiteSpace(TemplateTextBox.Text);
        bool hasTags = !string.IsNullOrWhiteSpace(TagsTextBox.Text);
        bool hasAnyInput = hasTemplate || hasTags;

        SaveAsButton.IsEnabled = hasTemplate && hasTags;
        CopyAllButton.IsEnabled = hasTemplate && hasTags;
        ClearButton.IsEnabled = hasAnyInput;
    }

    private void ShowValidationErrors(List<string> errors)
    {
        PreviewTabView.Visibility = Visibility.Collapsed;
        ValidationInfoBar.Visibility = Visibility.Visible;
        ValidationInfoBar.Message = string.Join(Environment.NewLine, errors);
        StatsText.Text = string.Empty;
    }

    private void ShowPreview()
    {
        ValidationInfoBar.Visibility = Visibility.Collapsed;
        PreviewTabView.Visibility = Visibility.Visible;
    }

    private const int MaxPreviewLines = 500;

    private async Task UpdatePreviewAsync()
    {
        string templateText = TemplateTextBox.Text;
        string tagsText = TagsTextBox.Text;

        PreviewTabView.TabItems.Clear();
        _sectionFullContent.Clear();
        ShowPreview();

        if (string.IsNullOrWhiteSpace(templateText) || string.IsNullOrWhiteSpace(tagsText))
        {
            StatsText.Text = string.Empty;
            return;
        }

        if (!templateText.Contains("{{TagName}}"))
        {
            ShowValidationErrors(["Template does not contain {{TagName}} — no replacements will be made."]);
            return;
        }

        var validationErrors = TemplateExpander.ValidateTemplate(templateText);
        if (validationErrors.Count > 0)
        {
            ShowValidationErrors(validationErrors);
            return;
        }

        PreviewProgressRing.IsActive = true;
        ProgressPercentText.Text = "0%";
        ProgressPanel.Visibility = Visibility.Visible;

        try
        {
            var sections = TemplateExpander.ParseTemplateSections(templateText);
            var tagNames = GetEffectiveTagNames(tagsText);
            int totalSections = sections.Count;

            var sectionResults = await Task.Run(() =>
            {
                var results = new (string Header, string Content)[totalSections];
                int completed = 0;

                Parallel.For(0, totalSections, i =>
                {
                    results[i] = TemplateExpander.ExpandSection(sections[i], tagNames);
                    int done = Interlocked.Increment(ref completed);
                    // Progress is reported but will be batched by the UI thread
                });

                return results;
            });

            for (int i = 0; i < sectionResults.Length; i++)
            {
                var (header, content) = sectionResults[i];

                // Truncate large content to keep the UI responsive
                bool truncated = false;
                string displayContent = content;
                int lineCount = CountLines(content);
                if (lineCount > MaxPreviewLines)
                {
                    displayContent = TruncateToLines(content, MaxPreviewLines);
                    truncated = true;
                }

                var textBox = new TextBox
                {
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    Text = displayContent
                };
                ScrollViewer.SetHorizontalScrollBarVisibility(textBox, ScrollBarVisibility.Hidden);
                ScrollViewer.SetHorizontalScrollMode(textBox, ScrollMode.Disabled);
                ScrollViewer.SetVerticalScrollBarVisibility(textBox, ScrollBarVisibility.Hidden);
                ScrollViewer.SetVerticalScrollMode(textBox, ScrollMode.Disabled);

                // Store full content for clipboard access
                _sectionFullContent[header] = content;

                var copyButton = new Button
                {
                    Content = "Copy Section",
                    Padding = new Thickness(12, 6, 12, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = header
                };
                copyButton.Click += CopySection_Click;

                var panel = new Grid();
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var topRow = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 0, 0, 4) };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                if (truncated)
                {
                    var infoBar = new InfoBar
                    {
                        IsOpen = true,
                        IsClosable = false,
                        Severity = InfoBarSeverity.Informational,
                        Message = $"Showing {MaxPreviewLines:N0} of {lineCount:N0} lines. Use Copy Section or Save As for full output."
                    };
                    Grid.SetColumn(infoBar, 0);
                    topRow.Children.Add(infoBar);
                }

                Grid.SetColumn(copyButton, 1);
                topRow.Children.Add(copyButton);

                Grid.SetRow(topRow, 0);
                panel.Children.Add(topRow);

                var scrollViewer = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = textBox
                };
                scrollViewer.SizeChanged += ScrollViewer_SizeChanged;
                Grid.SetRow(scrollViewer, 1);
                panel.Children.Add(scrollViewer);

                UIElement tabContent = panel;

                var tab = new TabViewItem
                {
                    Header = truncated ? $"{header} (truncated)" : header,
                    Content = tabContent,
                    IsClosable = false
                };
                PreviewTabView.TabItems.Add(tab);

                int percent = (int)((double)(i + 1) / sectionResults.Length * 100);
                ProgressPercentText.Text = $"{percent}%";
            }

            if (sectionResults.Length > 0)
                PreviewTabView.SelectedIndex = 0;

            var stats = await Task.Run(() => TemplateExpander.GetStats(templateText, tagsText));
            StatsText.Text = $"Tags: {stats.TagCount}  |  Replacements: {stats.ReplacementCount}  |  Output lines: {stats.OutputLineCount}";
        }
        catch
        {
            PreviewTabView.TabItems.Clear();
            StatsText.Text = string.Empty;
        }
        finally
        {
            PreviewProgressRing.IsActive = false;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int count = 1;
        int index = 0;
        while ((index = text.IndexOf('\n', index)) >= 0)
        {
            count++;
            index++;
        }
        return count;
    }

    private static string TruncateToLines(string text, int maxLines)
    {
        int index = 0;
        int linesFound = 0;
        while (linesFound < maxLines && index < text.Length)
        {
            int next = text.IndexOf('\n', index);
            if (next < 0)
                break;
            linesFound++;
            index = next + 1;
        }
        return text[..index];
    }

    private int _lastFindIndex;

    private void FindReplaceToggle_Changed(object sender, RoutedEventArgs e)
    {
        FindReplaceBar.Visibility = FindReplaceToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        _lastFindIndex = 0;

        if (FindReplaceToggle.IsChecked == true)
        {
            string selected = TemplateTextBox.SelectedText;
            if (!string.IsNullOrEmpty(selected))
            {
                FindBox.Text = selected;
            }
            FindBox.Focus(FocusState.Programmatic);
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        string find = FindBox.Text;
        if (string.IsNullOrEmpty(find))
            return;

        string text = TemplateTextBox.Text;
        int index = text.IndexOf(find, _lastFindIndex, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            _lastFindIndex = 0;
            index = text.IndexOf(find, _lastFindIndex, StringComparison.OrdinalIgnoreCase);
        }

        if (index >= 0)
        {
            TemplateTextBox.Select(index, find.Length);
            _lastFindIndex = index + find.Length;
            TemplateTextBox.Focus(FocusState.Programmatic);
        }
        else
        {
            ShowStatus("No matches found.", InfoBarSeverity.Informational);
        }
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        string find = FindBox.Text;
        string replace = ReplaceBox.Text;
        if (string.IsNullOrEmpty(find))
            return;

        string text = TemplateTextBox.Text;
        int start = TemplateTextBox.SelectionStart;
        int len = TemplateTextBox.SelectionLength;

        if (len == find.Length &&
            string.Equals(text.Substring(start, len), find, StringComparison.OrdinalIgnoreCase))
        {
            TemplateTextBox.Text = text.Remove(start, len).Insert(start, replace);
            TemplateTextBox.Select(start, replace.Length);
            _lastFindIndex = start + replace.Length;
        }

        FindNext_Click(sender, e);
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        string find = FindBox.Text;
        string replace = ReplaceBox.Text;
        if (string.IsNullOrEmpty(find))
            return;

        string text = TemplateTextBox.Text;
        string result = text.Replace(find, replace, StringComparison.OrdinalIgnoreCase);

        if (result != text)
        {
            TemplateTextBox.Text = result;
            _lastFindIndex = 0;
            ShowStatus("All occurrences replaced.", InfoBarSeverity.Success);
        }
        else
        {
            ShowStatus("No matches found.", InfoBarSeverity.Informational);
        }
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear All",
            Content = "Are you sure you want to clear all inputs and the preview?",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            TemplateTextBox.Text = string.Empty;
            TagsTextBox.Text = string.Empty;
            SortTagsCheckBox.IsChecked = false;
            PreviewTabView.TabItems.Clear();
            ShowPreview();
            FindBox.Text = string.Empty;
            ReplaceBox.Text = "{{TagName}}";
            StatusBar.IsOpen = false;
            UpdateButtonStates();
        }
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        string templateText = TemplateTextBox.Text;
        string tagsText = TagsTextBox.Text;

        if (string.IsNullOrWhiteSpace(templateText))
        {
            ShowStatus("Please enter template content.", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(tagsText))
        {
            ShowStatus("Please enter tag names.", InfoBarSeverity.Warning);
            return;
        }

        var path = await PickSaveFileAsync();
        if (path is null)
            return;

        try
        {
            string output = TemplateExpander.GeneratePreview(templateText, GetEffectiveTagsText(tagsText));
            await File.WriteAllTextAsync(path, output);
            ShowStatus($"Output saved successfully: {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void CopySection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string sectionHeader
            && _sectionFullContent.TryGetValue(sectionHeader, out string? content))
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(content);
            Clipboard.SetContent(dataPackage);
            ShowStatus($"Section '{sectionHeader}' copied to clipboard.", InfoBarSeverity.Success);
        }
    }

    private async void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        string templateText = TemplateTextBox.Text;
        string tagsText = TagsTextBox.Text;

        if (string.IsNullOrWhiteSpace(templateText) || string.IsNullOrWhiteSpace(tagsText))
        {
            ShowStatus("Please enter template and tag content.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            string fullOutput = await Task.Run(() =>
                TemplateExpander.GeneratePreview(templateText, GetEffectiveTagsText(tagsText)));

            var dataPackage = new DataPackage();
            dataPackage.SetText(fullOutput);
            Clipboard.SetContent(dataPackage);
            ShowStatus("Full output copied to clipboard.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        _statusTimer.Stop();
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;

        if (severity is InfoBarSeverity.Success or InfoBarSeverity.Informational)
        {
            _statusTimer.Start();
        }
    }

    private void ScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && sv.Content is TextBox tb)
        {
            tb.MinWidth = e.NewSize.Width;
            tb.MinHeight = e.NewSize.Height;
        }
    }

    private void TextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.Text.Length == 0)
            return;

        try
        {
            var rect = tb.GetRectFromCharacterIndex(tb.SelectionStart, false);
            if (rect.Width >= 0 && rect.Height >= 0)
            {
                tb.StartBringIntoView(new BringIntoViewOptions
                {
                    TargetRect = rect,
                    AnimationDesired = false
                });
            }
        }
        catch (ArgumentException)
        {
            // Can occur during initialization or rapid text changes
        }
    }

    private async Task<string?> PickSaveFileAsync()
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("Text Files", new List<string> { ".txt" });
        picker.SuggestedFileName = "Output";

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
