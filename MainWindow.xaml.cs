using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PandaChatbox;

public partial class MainWindow : Window
{
    readonly AppSettings _cfg;
    readonly Engine _eng;
    UIElement[]? _pages;
    bool _begun;

    // drag-to-reorder state
    Point _dragStart;
    StatusItem? _dragItem;

    public MainWindow(AppSettings cfg, List<StatusItem>? saved)
    {
        _cfg = cfg;
        _eng = new Engine(_cfg, new ObservableCollection<StatusItem>(saved ?? DefaultStatuses()));

        InitializeComponent();
        DataContext = _cfg;
        TextColorBox.Text = _cfg.TextColor;
        _pages = new UIElement[] { PageStatus, PageChat, PageSettings };
        StatusList.ItemsSource = _eng.Messages;
        ApplyWindowChrome();
        UpdateBackgroundSettings();

        Closing += (_, _) => SaveNow();
        StateChanged += (_, _) => UpdateMaximizeButton();

        // gentle fade-in when the window opens
        var root = (UIElement)Content;
        root.Opacity = 0;
        Loaded += (_, _) => root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    static List<StatusItem> DefaultStatuses() =>
        new[] { "i beat my boyfriend", "i love my bf", "i give my bf sloppy kisses" }
            .Select(t => new StatusItem { Text = t }).ToList();

    /// <summary>Starts the engine and timers. App.xaml.cs calls this while the splash screen is showing.</summary>
    public void Begin()
    {
        if (_begun) return;
        _begun = true;

        _eng.Start();

        // engine: ~20 times a second (sends only when something is due)
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        tick.Tick += (_, _) => _eng.Tick();
        tick.Start();

        // preview/labels: 4 times a second
        var ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        ui.Tick += (_, _) => RefreshUi();
        ui.Start();

        // autosave every 10 seconds (and once more on close)
        var save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        save.Tick += (_, _) => SaveNow();
        save.Start();

        RefreshUi();
    }

    void SaveNow() => SettingsStore.Save(_cfg, _eng.Messages);

    void ApplyWindowChrome()
    {
        WindowButtons.Visibility = _cfg.CustomWindowControls ? Visibility.Visible : Visibility.Collapsed;

        if (_cfg.CustomWindowControls)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
        }
        else
        {
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
        }

        UpdateMaximizeButton();
    }

    void UpdateMaximizeButton()
    {
        if (MaximizeButton == null) return;
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    void Header_DragMove(object sender, MouseButtonEventArgs e)
    {
        if (_cfg.CustomWindowControls && e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------- tabs ----------------
    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_pages == null || sender is not RadioButton rb || rb.Tag is not string tag) return;
        int idx = int.Parse(tag);
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
        // soft fade-in for the page you switch to
        _pages[idx].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    // ---------------- preview / live labels ----------------
    void RefreshUi()
    {
        string msg = _eng.Compose();
        int n = msg.Length;
        bool over = n > Engine.ChatboxLimit;
        PreviewText.Text = msg.Length == 0 ? "(nothing selected)" : msg;
        CountText.Text = $"{n}/{Engine.ChatboxLimit}";
        CountText.Foreground = (Brush)FindResource(over ? "Bad" : "Muted");
        OverWarning.Visibility = over ? Visibility.Visible : Visibility.Collapsed;

        string err = _eng.ConnectionError;
        bool isErr = err.Length > 0;
        ConnText.Text = isErr ? "Error: " + err : $"Sending to {_cfg.Ip}:{_cfg.Port}";
        ConnText.Foreground = (Brush)FindResource(isErr ? "Bad" : "Lilac");

        SendBadge.Text = _cfg.Send ? "Running" : "Paused";
        SendBadge.Foreground = (Brush)FindResource(_cfg.Send ? "Green" : "Muted");
        SendDot.Fill = (Brush)FindResource(_cfg.Send ? "Green" : "Off");

        WeatherStatusText.Text = _eng.WeatherStatus;
        MediaStatusText.Text = _eng.MediaStatus;

        CpuDetectedText.Text = _eng.Hardware.CpuName;
        RamDetectedText.Text = $"{_eng.Hardware.TotalRamGb:0.0} GB";
        GpuDetectedText.Text = _eng.Hardware.GpuName;
        VramDetectedText.Text = $"{_eng.Hardware.TotalVramGb:0.0} GB dedicated";

        int total = _eng.Messages.Count;
        int on = _eng.Messages.Count(m => m.Enabled);
        StatusSummary.Text = total == 0 ? "" : $"{on} of {total} in rotation";
        EmptyHint.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- side panel + header ----------------
    void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_cfg.Send) _eng.Kick(); else _eng.SendClear();
    }
    void SendNow_Click(object sender, RoutedEventArgs e) => _eng.SendNow();
    void Clear_Click(object sender, RoutedEventArgs e) => _eng.SendClear();

    // ---------------- status list ----------------
    static StatusItem? Item(object sender) => (sender as FrameworkElement)?.DataContext as StatusItem;

    void Add_Click(object sender, RoutedEventArgs e)
    {
        string t = StatusEdit.Text.Trim();
        if (t.Length == 0) return;
        _eng.Messages.Add(new StatusItem { Text = t });
        StatusEdit.Clear();
        _eng.StatusChanged();
        Dispatcher.BeginInvoke(new Action(() => StatusScroll.ScrollToEnd()), DispatcherPriority.Background);
    }

    void StatusEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Add_Click(sender, e); e.Handled = true; }
    }

    void StatusEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Placeholder == null || CreateCount == null) return;
        int n = StatusEdit.Text.Length;
        Placeholder.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        CreateCount.Text = $"{n}/{Engine.ChatboxLimit}";
    }

    void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_eng.Messages.Count == 0) return;
        var r = MessageBox.Show(this, "Remove all status messages?", "Panda Chatbox",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        _eng.Messages.Clear();
        _eng.StatusChanged();
    }

    // power / heart toggles
    void RowChanged_Click(object sender, RoutedEventArgs e) => _eng.StatusChanged();

    void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is not { } it) return;
        _eng.Messages.Remove(it);
        _eng.StatusChanged();
    }

    // inline editing (pencil button or double-click)
    void FinishEdit(StatusItem it)
    {
        it.IsEditing = false;
        string t = it.Text.Trim();
        if (t.Length == 0) _eng.Messages.Remove(it);   // blank = delete
        else it.Text = t;
        _eng.StatusChanged();
    }

    // pencil button: when it switches editing OFF, save the text the same way Enter does
    void EditToggle_Click(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { IsEditing: false } it) FinishEdit(it);
    }

    void Edit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { IsEditing: true } it) FinishEdit(it);
    }

    void Edit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape) return;
        if (Item(sender) is { } it) FinishEdit(it);
        e.Handled = true;
    }

    void Edit_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
            tb.Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); tb.SelectAll(); }), DispatcherPriority.Input);
    }

    // drag a row to reorder
    void Text_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Item(sender) is not { } it || it.IsEditing) return;
        if (e.ClickCount == 2) { it.IsEditing = true; e.Handled = true; return; }
        _dragItem = it;
        _dragStart = e.GetPosition(null);
    }

    void Text_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
        if (Item(sender) is not { } it || it != _dragItem || it.IsEditing) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragItem = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(StatusItem), it), DragDropEffects.Move);
    }

    void Row_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(StatusItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void Row_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(StatusItem)) is StatusItem src && Item(sender) is { } dst && src != dst)
        {
            int from = _eng.Messages.IndexOf(src), to = _eng.Messages.IndexOf(dst);
            if (from >= 0 && to >= 0) _eng.Messages.Move(from, to);
        }
        e.Handled = true;
    }

    // ---------------- settings ----------------
    void StatusMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _eng.StatusChanged();
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyThemeSettings();
    }

    void SurfaceTransparency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        ApplyThemeSettings();
    }

    void ApplyTextColor_Click(object sender, RoutedEventArgs e)
    {
        string color = TextColorBox.Text.Trim();
        if (!ThemeManager.IsValidTextColor(color))
        {
            MessageBox.Show(this, "Enter a hex color in #RRGGBB format, such as #FFFFFF.",
                "Panda Chatbox", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cfg.TextColor = color;
        ApplyThemeSettings();
    }

    void BackgroundMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateBackgroundSettings();
        ApplyThemeSettings();
    }

    void BackgroundColor_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _cfg.BackgroundMode != 1) return;
        UpdateBackgroundPreview();
        ApplyThemeSettings(save: false);
    }

    void UpdateBackgroundSettings()
    {
        if (SolidColorSettings == null) return;
        SolidColorSettings.Visibility = _cfg.BackgroundMode == 1 ? Visibility.Visible : Visibility.Collapsed;
        PictureSettings.Visibility = _cfg.BackgroundMode == 2 ? Visibility.Visible : Visibility.Collapsed;
        BackgroundImageName.Text = string.IsNullOrWhiteSpace(_cfg.BackgroundImagePath)
            ? "Using the theme background until you choose a picture"
            : System.IO.Path.GetFileName(_cfg.BackgroundImagePath);
        BackgroundImageName.ToolTip = _cfg.BackgroundImagePath;
        UpdateBackgroundPreview();
    }

    void UpdateBackgroundPreview()
    {
        if (BackgroundColorPreview == null) return;
        var preview = ThemeManager.PreviewColor(_cfg);
        BackgroundColorPreview.Background = new SolidColorBrush(preview);
        BackgroundColorHex.Text = preview.ToString();
    }

    bool ApplyThemeSettings(bool save = true)
    {
        try
        {
            ThemeManager.Apply(_cfg);
            if (save) SaveNow();
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or FormatException or InvalidOperationException)
        {
            MessageBox.Show(this, "The background couldn't be loaded:\n\n" + ex.Message, "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _cfg.BackgroundMode = 0;
            UpdateBackgroundSettings();
            ThemeManager.Apply(_cfg);
            SaveNow();
            return false;
        }
    }

    void ChooseBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a background picture",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        string oldPath = _cfg.BackgroundImagePath;
        int oldMode = _cfg.BackgroundMode;
        _cfg.BackgroundImagePath = dialog.FileName;
        _cfg.BackgroundMode = 2;
        UpdateBackgroundSettings();
        if (!ApplyThemeSettings())
        {
            _cfg.BackgroundImagePath = oldPath;
            _cfg.BackgroundMode = oldMode;
            UpdateBackgroundSettings();
            ThemeManager.Apply(_cfg);
            SaveNow();
        }
    }

    void CustomWindowControls_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyWindowChrome();
        SaveNow();
    }

    void ExpandAll_Click(object sender, RoutedEventArgs e) => SetAllSections(true);
    void CollapseAll_Click(object sender, RoutedEventArgs e) => SetAllSections(false);
    void SetAllSections(bool open)
    {
        foreach (var ex in SettingsStack.Children.OfType<Expander>()) ex.IsExpanded = open;
    }

    void Apply_Click(object sender, RoutedEventArgs e) => _eng.ApplyConnection();

    // ---------------- chat ----------------
    void SendChat_Click(object sender, RoutedEventArgs e)
    {
        string t = ChatEdit.Text.Trim();
        if (t.Length == 0) return;
        _eng.SendChat(t);
        ChatEdit.Clear();
    }
    void StopChat_Click(object sender, RoutedEventArgs e) => _eng.StopChat();
    void ChatEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { SendChat_Click(sender, e); e.Handled = true; }
    }
}
