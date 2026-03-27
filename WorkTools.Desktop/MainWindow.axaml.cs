using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WorkTools.Core;

namespace WorkTools.Desktop;

public partial class MainWindow : Window
{
    private enum StatusSeverity
    {
        Success,
        Info,
        Warning,
        Error
    }

    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dictionary<string, string> _sectionFullContent = new();
    private const int MaxPreviewLines = 500;
    private int _lastFindIndex;

    public MainWindow()
    {
        InitializeComponent();
        ReplaceBox.Text = "{{TagName}}";

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounceTimer.Tick += DebounceTimer_Tick;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += StatusTimer_Tick;
    }

    private async void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        await UpdatePreviewAsync();
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        StatusBorder.IsVisible = false;
    }

    private void Input_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
        UpdateButtonStates();
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
        ValidationBorder.IsVisible = true;
        ValidationText.Text = string.Join(Environment.NewLine, errors);
        PreviewTabControl.IsVisible = false;
        StatsText.Text = string.Empty;
    }

    private void ShowPreview()
    {
        ValidationBorder.IsVisible = false;
        PreviewTabControl.IsVisible = true;
    }

    private async Task UpdatePreviewAsync()
    {
        string templateText = TemplateTextBox.Text ?? string.Empty;
        string tagsText = TagsTextBox.Text ?? string.Empty;

        PreviewTabControl.ItemsSource = null;
        _sectionFullContent.Clear();
        ShowPreview();

        if (string.IsNullOrWhiteSpace(templateText) || string.IsNullOrWhiteSpace(tagsText))
        {
            StatsText.Text = string.Empty;
            return;
        }

        if (!templateText.Contains("{{TagName}}", StringComparison.Ordinal))
        {
            ShowValidationErrors(["Template does not contain {{TagName}} - no replacements will be made."]);
            return;
        }

        var validationErrors = TemplateExpander.ValidateTemplate(templateText);
        if (validationErrors.Count > 0)
        {
            ShowValidationErrors(validationErrors);
            return;
        }

        ProgressBar.IsVisible = true;
        ProgressText.IsVisible = true;
        ProgressBar.Value = 0;
        ProgressText.Text = "0%";

        try
        {
            var sections = TemplateExpander.ParseTemplateSections(templateText);
            var tagNames = TemplateExpander.ParseTagNames(tagsText);

            var sectionResults = await Task.Run(() =>
            {
                var results = new (string Header, string Content)[sections.Count];
                Parallel.For(0, sections.Count, i =>
                {
                    results[i] = TemplateExpander.ExpandSection(sections[i], tagNames);
                });
                return results;
            });

            var tabs = new List<TabItem>(sectionResults.Length);
            for (int i = 0; i < sectionResults.Length; i++)
            {
                var (header, content) = sectionResults[i];
                _sectionFullContent[header] = content;

                int lineCount = CountLines(content);
                bool truncated = lineCount > MaxPreviewLines;
                string displayContent = truncated ? TruncateToLines(content, MaxPreviewLines) : content;

                tabs.Add(BuildSectionTab(header, displayContent, truncated, lineCount));

                int percent = (int)((i + 1d) / sectionResults.Length * 100);
                ProgressBar.Value = percent;
                ProgressText.Text = $"{percent}%";
            }

            PreviewTabControl.ItemsSource = tabs;

            var stats = await Task.Run(() => TemplateExpander.GetStats(templateText, tagsText));
            StatsText.Text = $"Tags: {stats.TagCount}  |  Replacements: {stats.ReplacementCount}  |  Output lines: {stats.OutputLineCount}";
        }
        catch (Exception ex)
        {
            PreviewTabControl.ItemsSource = null;
            StatsText.Text = string.Empty;
            ShowStatus($"Error: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            ProgressBar.IsVisible = false;
            ProgressText.IsVisible = false;
        }
    }

    private TabItem BuildSectionTab(string header, string content, bool truncated, int lineCount)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8
        };

        var top = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };

        if (truncated)
        {
            top.Children.Add(new TextBlock
            {
                Text = $"Showing {MaxPreviewLines:N0} of {lineCount:N0} lines. Copy Section or Save As for full output.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Opacity = 0.8
            });
        }

        var copyButton = new Button { Content = "Copy Section", Tag = header };
        copyButton.Click += CopySection_Click;
        Grid.SetColumn(copyButton, 1);
        top.Children.Add(copyButton);

        Grid.SetRow(top, 0);
        root.Children.Add(top);

        var textBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            Text = content
        };
        Grid.SetRow(textBox, 1);
        root.Children.Add(textBox);

        return new TabItem
        {
            Header = truncated ? $"{header} (truncated)" : header,
            Content = root
        };
    }

    private async void SaveAs_Click(object? sender, RoutedEventArgs e)
    {
        string templateText = TemplateTextBox.Text ?? string.Empty;
        string tagsText = TagsTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(templateText) || string.IsNullOrWhiteSpace(tagsText))
        {
            ShowStatus("Please enter template and tag content.", StatusSeverity.Warning);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            ShowStatus("Save dialog is unavailable in this environment.", StatusSeverity.Warning);
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "Output.txt",
            FileTypeChoices =
            [
                new FilePickerFileType("Text files")
                {
                    Patterns = ["*.txt"]
                }
            ]
        });

        if (file is null)
        {
            return;
        }

        try
        {
            string output = await Task.Run(() => TemplateExpander.GeneratePreview(templateText, tagsText));
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(output);

            ShowStatus("Output saved successfully.", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", StatusSeverity.Error);
        }
    }

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        string templateText = TemplateTextBox.Text ?? string.Empty;
        string tagsText = TagsTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(templateText) || string.IsNullOrWhiteSpace(tagsText))
        {
            ShowStatus("Please enter template and tag content.", StatusSeverity.Warning);
            return;
        }

        try
        {
            string output = await Task.Run(() => TemplateExpander.GeneratePreview(templateText, tagsText));
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                ShowStatus("Clipboard is unavailable in this environment.", StatusSeverity.Warning);
                return;
            }

            await clipboard.SetTextAsync(output);
            ShowStatus("Full output copied to clipboard.", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", StatusSeverity.Error);
        }
    }

    private async void CopySection_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string header)
        {
            return;
        }

        if (!_sectionFullContent.TryGetValue(header, out string? content))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowStatus("Clipboard is unavailable in this environment.", StatusSeverity.Warning);
            return;
        }

        await clipboard.SetTextAsync(content);
        ShowStatus($"Section '{header}' copied to clipboard.", StatusSeverity.Success);
    }

    private async void Clear_Click(object? sender, RoutedEventArgs e)
    {
        bool confirmed = await ConfirmClearAsync();
        if (!confirmed)
        {
            return;
        }

        TemplateTextBox.Text = string.Empty;
        TagsTextBox.Text = string.Empty;
        FindBox.Text = string.Empty;
        ReplaceBox.Text = "{{TagName}}";
        _lastFindIndex = 0;
        PreviewTabControl.ItemsSource = null;
        _sectionFullContent.Clear();
        ValidationBorder.IsVisible = false;
        StatsText.Text = string.Empty;
        StatusBorder.IsVisible = false;
        UpdateButtonStates();
    }

    private void FindNext_Click(object? sender, RoutedEventArgs e)
    {
        string find = FindBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(find))
        {
            return;
        }

        string text = TemplateTextBox.Text ?? string.Empty;
        int index = text.IndexOf(find, _lastFindIndex, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            _lastFindIndex = 0;
            index = text.IndexOf(find, _lastFindIndex, StringComparison.OrdinalIgnoreCase);
        }

        if (index >= 0)
        {
            TemplateTextBox.SelectionStart = index;
            TemplateTextBox.SelectionEnd = index + find.Length;
            TemplateTextBox.CaretIndex = index + find.Length;
            TemplateTextBox.Focus();
            _lastFindIndex = index + find.Length;
            return;
        }

        ShowStatus("No matches found.", StatusSeverity.Info);
    }

    private void Replace_Click(object? sender, RoutedEventArgs e)
    {
        string find = FindBox.Text ?? string.Empty;
        string replace = ReplaceBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(find))
        {
            return;
        }

        string text = TemplateTextBox.Text ?? string.Empty;
        int start = Math.Min(TemplateTextBox.SelectionStart, TemplateTextBox.SelectionEnd);
        int end = Math.Max(TemplateTextBox.SelectionStart, TemplateTextBox.SelectionEnd);
        int length = end - start;

        if (length == find.Length &&
            string.Equals(text.Substring(start, length), find, StringComparison.OrdinalIgnoreCase))
        {
            string updated = text.Remove(start, length).Insert(start, replace);
            TemplateTextBox.Text = updated;
            TemplateTextBox.SelectionStart = start;
            TemplateTextBox.SelectionEnd = start + replace.Length;
            TemplateTextBox.CaretIndex = start + replace.Length;
            _lastFindIndex = start + replace.Length;
        }

        FindNext_Click(sender, e);
    }

    private void ReplaceAll_Click(object? sender, RoutedEventArgs e)
    {
        string find = FindBox.Text ?? string.Empty;
        string replace = ReplaceBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(find))
        {
            return;
        }

        string text = TemplateTextBox.Text ?? string.Empty;
        string result = text.Replace(find, replace, StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(result, text, StringComparison.Ordinal))
        {
            TemplateTextBox.Text = result;
            _lastFindIndex = 0;
            ShowStatus("All occurrences replaced.", StatusSeverity.Success);
            return;
        }

        ShowStatus("No matches found.", StatusSeverity.Info);
    }

    private void ShowStatus(string message, StatusSeverity severity)
    {
        StatusText.Text = message;
        ApplyStatusVisuals(severity);
        StatusBorder.IsVisible = true;

        _statusTimer.Stop();
        if (severity is StatusSeverity.Success or StatusSeverity.Info)
        {
            _statusTimer.Start();
        }
    }

    private void ApplyStatusVisuals(StatusSeverity severity)
    {
        switch (severity)
        {
            case StatusSeverity.Success:
                StatusBorder.Background = new SolidColorBrush(Color.Parse("#2232A852"));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#6642C56A"));
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#FFE8F8EE"));
                break;
            case StatusSeverity.Info:
                StatusBorder.Background = new SolidColorBrush(Color.Parse("#223072F5"));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#553072F5"));
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#FFEAF0FF"));
                break;
            case StatusSeverity.Warning:
                StatusBorder.Background = new SolidColorBrush(Color.Parse("#22B87500"));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#66DE9C1C"));
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#FFFFF4E1"));
                break;
            default:
                StatusBorder.Background = new SolidColorBrush(Color.Parse("#22B3261E"));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.Parse("#66D13E37"));
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#FFFFEDEA"));
                break;
        }
    }

    private async Task<bool> ConfirmClearAsync()
    {
        bool? decision = null;

        var dialog = new Window
        {
            Title = "Clear All",
            Width = 420,
            Height = 180,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var message = new TextBlock
        {
            Text = "Are you sure you want to clear all inputs and the preview?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 92
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 92,
            IsDefault = true,
            IsCancel = true
        };

        clearButton.Click += (_, _) =>
        {
            decision = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            decision = false;
            dialog.Close();
        };

        dialog.Closing += (_, _) =>
        {
            decision ??= false;
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { clearButton, cancelButton }
        };

        dialog.Content = new Border
        {
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Children = { message, buttonRow }
            }
        };

        await dialog.ShowDialog(this);
        return DialogDecision.IsConfirmed(decision);
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
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
            {
                break;
            }

            linesFound++;
            index = next + 1;
        }

        return text[..Math.Min(index, text.Length)];
    }
}
