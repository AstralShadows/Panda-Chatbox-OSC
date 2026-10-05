using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PandaChatbox;

public partial class TemplateBuilderWindow : Window
{
    static readonly IReadOnlyDictionary<string, string> Examples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["time"] = "4:32 PM",
        ["date"] = "Mon, Oct 5",
        ["app"] = "VRChat",
        ["game"] = "VRChat",
        ["song"] = "Artist - Song title (Spotify)",
        ["player"] = "Spotify",
        ["cpu"] = "24%",
        ["ram"] = "12.5/32GB",
        ["vram"] = "4/8GB",
        ["battery"] = "85% (charging)",
        ["uptime"] = "2d 4h"
    };

    public string TemplateText => TemplateTextBox.Text;

    public TemplateBuilderWindow(string initialText)
    {
        InitializeComponent();
        foreach (string token in new[] { "{time}", "{date}", "{app}", "{game}", "{song}", "{player}", "{cpu}", "{ram}", "{vram}", "{battery}", "{uptime}" })
            TokenList.Items.Add(new ListBoxItem { Content = token });
        TemplateTextBox.Text = initialText;
        TemplateTextBox.CaretIndex = TemplateTextBox.Text.Length;
        RefreshPreview();
    }

    void TemplateTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    void TokenList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InsertTokenButton.IsEnabled = TokenList.SelectedItem is ListBoxItem;

    void InsertToken_Click(object sender, RoutedEventArgs e)
    {
        if (TokenList.SelectedItem is not ListBoxItem { Content: string token }) return;
        int selectionStart = TemplateTextBox.SelectionStart;
        string updatedText = TemplateTextBox.Text.Remove(selectionStart, TemplateTextBox.SelectionLength)
            .Insert(selectionStart, token);
        TemplateTextBox.Text = updatedText;
        TemplateTextBox.CaretIndex = selectionStart + token.Length;
        TemplateTextBox.Focus();
    }

    void UseTemplate_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void RefreshPreview()
    {
        string expanded = StatusTemplate.Expand(TemplateTextBox.Text, Examples);
        ExpandedPreview.Text = expanded.Length == 0 ? "(empty template)" : expanded;
        CharacterCount.Text = $"Example output: {expanded.Length}/144 characters";
        LimitWarning.Visibility = expanded.Length > Engine.ChatboxLimit ? Visibility.Visible : Visibility.Collapsed;
        CharacterCount.Foreground = (System.Windows.Media.Brush)FindResource(
            expanded.Length > Engine.ChatboxLimit ? "Bad" : "Muted");
    }
}
