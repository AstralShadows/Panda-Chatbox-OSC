using System;
using System.Windows;
using System.Windows.Input;

namespace PandaChatbox;

public partial class ChangelogNoticeWindow : Window
{
    public bool OpenReleaseRequested { get; private set; }

    public ChangelogNoticeWindow(Version version, string notes)
    {
        InitializeComponent();
        VersionText.Text = $"Version {version}";
        NotesText.Text = string.IsNullOrWhiteSpace(notes)
            ? "No changelog was included with this release."
            : notes.Trim();
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void ViewRelease_Click(object sender, RoutedEventArgs e)
    {
        OpenReleaseRequested = true;
        Close();
    }
}
