using System;
using System.Windows;
using System.Windows.Input;

namespace PandaChatbox;

public partial class UpdateNoticeWindow : Window
{
    public bool DontShowAgain { get; private set; }
    public bool OpenReleaseRequested { get; private set; }

    public UpdateNoticeWindow(Version latestVersion, Version currentVersion)
    {
        InitializeComponent();
        VersionText.Text = $"Installed: v{currentVersion}   ·   Available: v{latestVersion}";
    }

    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void DontShowAgain_Click(object sender, RoutedEventArgs e)
    {
        DontShowAgain = true;
        Close();
    }

    void ViewUpdate_Click(object sender, RoutedEventArgs e)
    {
        OpenReleaseRequested = true;
        Close();
    }
}
