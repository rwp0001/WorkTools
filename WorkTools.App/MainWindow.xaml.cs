using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Windows.Storage.Pickers;
using WorkTools.Core;
using WinRT.Interop;

namespace WorkTools.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _debounceTimer;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "WorkTools - Template Expander";

        _debounceTimer = DispatcherQueue.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += (_, _) => UpdatePreview();

        TemplateScroll.SizeChanged += ScrollViewer_SizeChanged;
        TagsScroll.SizeChanged += ScrollViewer_SizeChanged;
        PreviewScroll.SizeChanged += ScrollViewer_SizeChanged;
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
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
        ClearButton.IsEnabled = hasAnyInput;
    }

    private void UpdatePreview()
    {
        string templateText = TemplateTextBox.Text;
        string tagsText = TagsTextBox.Text;

        if (string.IsNullOrWhiteSpace(templateText) || string.IsNullOrWhiteSpace(tagsText))
        {
            PreviewTextBox.Text = string.Empty;
            StatsText.Text = string.Empty;
            return;
        }

        if (!templateText.Contains("{{TagName}}"))
        {
            PreviewTextBox.Text = string.Empty;
            StatsText.Text = "Template does not contain {{TagName}} — no replacements will be made.";
            return;
        }

        try
        {
            PreviewTextBox.Text = TemplateExpander.GeneratePreview(templateText, tagsText);
            var (tagCount, replacementCount, outputLineCount) = TemplateExpander.GetStats(templateText, tagsText);
            StatsText.Text = $"Tags: {tagCount}  |  Replacements: {replacementCount}  |  Output lines: {outputLineCount}";
        }
        catch
        {
            PreviewTextBox.Text = string.Empty;
            StatsText.Text = string.Empty;
        }
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
            PreviewTextBox.Text = string.Empty;
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
            TemplateExpander.GenerateFromText(templateText, tagsText, path);
            ShowStatus($"Output saved successfully: {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
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
