using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Text;
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
    private readonly List<TextBox> _replacementTextBoxes = [];
    private readonly StackPanel _replacementColumnsPanel;
    private readonly Border _l5kDropBorder;
    private readonly TextBox _l5kTemplateTextBox;
    private const int MaxPreviewLines = 500;
    private int _lastFindIndex;

    public MainWindow()
    {
        InitializeComponent();
        _replacementColumnsPanel = this.FindControl<StackPanel>("ReplacementColumnsPanel")
            ?? throw new InvalidOperationException("ReplacementColumnsPanel was not found.");
        _l5kDropBorder = this.FindControl<Border>("L5kDropBorder")
            ?? throw new InvalidOperationException("L5kDropBorder was not found.");
        _l5kTemplateTextBox = this.FindControl<TextBox>("L5kTemplateTextBox")
            ?? throw new InvalidOperationException("L5kTemplateTextBox was not found.");

        DragDrop.SetAllowDrop(_l5kDropBorder, true);
        _l5kDropBorder.AddHandler(DragDrop.DragOverEvent, L5kDropBorder_DragOver);
        _l5kDropBorder.AddHandler(DragDrop.DropEvent, L5kDropBorder_Drop);

        ReplaceBox.Text = "{{1}}";
        _replacementTextBoxes.Add(TagsTextBox);

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
        bool hasReplacementInput = _replacementTextBoxes.Any(textBox => !string.IsNullOrWhiteSpace(textBox.Text));
        bool hasAnyInput = hasTemplate || hasReplacementInput;

        SaveAsButton.IsEnabled = hasTemplate && hasReplacementInput;
        CopyAllButton.IsEnabled = hasTemplate && hasReplacementInput;
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
        bool hasReplacementInput = _replacementTextBoxes.Any(textBox => !string.IsNullOrWhiteSpace(textBox.Text));

        PreviewTabControl.ItemsSource = null;
        _sectionFullContent.Clear();
        ShowPreview();

        if (string.IsNullOrWhiteSpace(templateText) || !hasReplacementInput)
        {
            StatsText.Text = string.Empty;
            return;
        }

        if (!TemplateExpander.ContainsReplacementPlaceholders(templateText))
        {
            ShowValidationErrors(["Template does not contain any replacement placeholders like {{1}}."]);
            return;
        }

        var validationErrors = TemplateExpander.ValidateTemplate(templateText);
        if (validationErrors.Count > 0)
        {
            ShowValidationErrors(validationErrors);
            return;
        }

        var replacementColumns = GetReplacementColumns();
        var replacementColumnWarnings = ValidateReplacementColumnCounts(replacementColumns);
        if (replacementColumnWarnings.Count > 0)
        {
            ShowValidationErrors(replacementColumnWarnings);
            return;
        }

        string tagsText = BuildReplacementRowsText(replacementColumns);
        var replacementErrors = TemplateExpander.ValidateReplacementData(templateText, tagsText);
        if (replacementErrors.Count > 0)
        {
            ShowValidationErrors(replacementErrors);
            return;
        }

        ProgressBar.IsVisible = true;
        ProgressText.IsVisible = true;
        ProgressBar.Value = 0;
        ProgressText.Text = "0%";

        try
        {
            var sections = TemplateExpander.ParseTemplateSections(templateText);
            var replacementRows = TemplateExpander.ParseReplacementRows(tagsText);

            var sectionResults = await Task.Run(() =>
            {
                var results = new (string Header, string Content)[sections.Count];
                Parallel.For(0, sections.Count, i =>
                {
                    results[i] = TemplateExpander.ExpandSection(sections[i], replacementRows);
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
            StatsText.Text = $"Rows: {stats.TagCount}  |  Replacements: {stats.ReplacementCount}  |  Output lines: {stats.OutputLineCount}";
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

    private void AddReplacementColumn_Click(object? sender, RoutedEventArgs e)
    {
        int placeholderIndex = _replacementTextBoxes.Count + 1;
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
        };
        textBox.TextChanged += Input_TextChanged;

        var placeholderLabel = new TextBlock
        {
            Text = $"{{{{{placeholderIndex}}}}}",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var removeButton = new Button
        {
            Content = "×",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Tag = textBox
        };
        removeButton.Click += RemoveReplacementColumn_Click;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6,
            MinHeight = 28
        };
        header.Children.Add(placeholderLabel);
        Grid.SetColumn(removeButton, 1);
        header.Children.Add(removeButton);

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 4,
            Width = 240,
            Tag = placeholderLabel
        };
        column.Children.Add(header);
        Grid.SetRow(textBox, 1);
        column.Children.Add(textBox);

        _replacementColumnsPanel.Children.Add(column);
        _replacementTextBoxes.Add(textBox);
        textBox.Focus();
        UpdateButtonStates();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void RemoveReplacementColumn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TextBox textBox })
            return;

        int textBoxIndex = _replacementTextBoxes.IndexOf(textBox);
        if (textBoxIndex <= 0)
            return;

        _replacementTextBoxes.RemoveAt(textBoxIndex);
        _replacementColumnsPanel.Children.RemoveAt(textBoxIndex);
        UpdateReplacementColumnHeaders();
        UpdateButtonStates();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void UpdateReplacementColumnHeaders()
    {
        for (int i = 1; i < _replacementColumnsPanel.Children.Count; i++)
        {
            if (_replacementColumnsPanel.Children[i] is Grid { Tag: TextBlock placeholderLabel })
                placeholderLabel.Text = $"{{{{{i + 1}}}}}";
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
        bool hasReplacementInput = _replacementTextBoxes.Any(textBox => !string.IsNullOrWhiteSpace(textBox.Text));

        if (string.IsNullOrWhiteSpace(templateText) || !hasReplacementInput)
        {
            ShowStatus("Please enter template and replacement content.", StatusSeverity.Warning);
            return;
        }

        var replacementColumns = GetReplacementColumns();
        var replacementColumnWarnings = ValidateReplacementColumnCounts(replacementColumns);
        if (replacementColumnWarnings.Count > 0)
        {
            ShowValidationErrors(replacementColumnWarnings);
            return;
        }

        string tagsText = BuildReplacementRowsText(replacementColumns);

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
        bool hasReplacementInput = _replacementTextBoxes.Any(textBox => !string.IsNullOrWhiteSpace(textBox.Text));

        if (string.IsNullOrWhiteSpace(templateText) || !hasReplacementInput)
        {
            ShowStatus("Please enter template and replacement content.", StatusSeverity.Warning);
            return;
        }

        var replacementColumns = GetReplacementColumns();
        var replacementColumnWarnings = ValidateReplacementColumnCounts(replacementColumns);
        if (replacementColumnWarnings.Count > 0)
        {
            ShowValidationErrors(replacementColumnWarnings);
            return;
        }

        string tagsText = BuildReplacementRowsText(replacementColumns);

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
        foreach (var textBox in _replacementTextBoxes)
            textBox.Text = string.Empty;
        while (_replacementTextBoxes.Count > 1)
        {
            _replacementTextBoxes.RemoveAt(_replacementTextBoxes.Count - 1);
            _replacementColumnsPanel.Children.RemoveAt(_replacementColumnsPanel.Children.Count - 1);
        }
        FindBox.Text = string.Empty;
        ReplaceBox.Text = "{{1}}";
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

    private string[][] GetReplacementColumns()
    {
        return _replacementTextBoxes
            .Select(textBox => SplitReplacementEntries(textBox.Text))
            .ToArray();
    }

    private static string[] SplitReplacementEntries(string? text)
    {
        return (text ?? string.Empty)
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static List<string> ValidateReplacementColumnCounts(string[][] replacementColumns)
    {
        var warnings = new List<string>();
        if (replacementColumns.Length <= 1)
            return warnings;

        int expectedCount = replacementColumns[0].Length;
        for (int i = 1; i < replacementColumns.Length; i++)
        {
            int actualCount = replacementColumns[i].Length;
            if (actualCount != expectedCount)
            {
                warnings.Add($"Placeholder {{{{{i + 1}}}}} has {actualCount} entr{(actualCount == 1 ? "y" : "ies")}, but {{1}} has {expectedCount}.");
            }
        }

        return warnings;
    }

    private static string BuildReplacementRowsText(string[][] replacementColumns)
    {
        if (replacementColumns.Length == 0 || replacementColumns[0].Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        int rowCount = replacementColumns[0].Length;
        for (int row = 0; row < rowCount; row++)
        {
            if (row > 0)
                sb.AppendLine();

            for (int column = 0; column < replacementColumns.Length; column++)
            {
                if (column > 0)
                    sb.Append('\t');
                sb.Append(replacementColumns[column][row]);
            }
        }

        return sb.ToString();
    }

    private async void L5kDropBorder_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        var files = e.Data.GetFiles()?.OfType<IStorageFile>().ToList();
        if (files is null || files.Count == 0)
        {
            ShowStatus("Drop a valid L5K or L5X file.", StatusSeverity.Warning);
            return;
        }

        IStorageFile? targetFile = files.FirstOrDefault(file => file.Name.EndsWith(".l5k", StringComparison.OrdinalIgnoreCase)
                                                          || file.Name.EndsWith(".l5x", StringComparison.OrdinalIgnoreCase))
        ?? files.FirstOrDefault();

        if (targetFile is null)
        {
            ShowStatus("Drop a valid L5K or L5X file.", StatusSeverity.Warning);
            return;
        }

        try
        {
            await using var stream = await targetFile.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            string l5kText = await reader.ReadToEndAsync();

            string template = L5kTemplateGenerator.GenerateCrimsonTemplate(l5kText);
            _l5kTemplateTextBox.Text = template;
            _l5kTemplateTextBox.IsVisible = true;
            _l5kDropBorder.IsVisible = false;

            ShowStatus("Template generated from L5K/L5X successfully.", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", StatusSeverity.Error);
        }
    }

    private void L5kDropBorder_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasDroppedFile(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool HasDroppedFile(IDataObject data)
    {
        var files = data.GetFiles();
        return files is not null && files.Any();
    }
}
