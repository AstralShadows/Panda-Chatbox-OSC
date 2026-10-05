using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
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
    bool _updateCheckInProgress;
    bool _updatingProfileUi;
    bool _updateAvailable;
    bool _updateNoticeShown;
    bool _openReleasePageOnClick;
    Uri? _latestReleaseUri;
    Version? _latestReleaseVersion;
    DispatcherTimer? _updateTimer;
    readonly CancellationTokenSource _updateCancellation = new();

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
        _pages = new UIElement[] { PageStatus, PageChat, PageIntegrations, PageSettings };
        StatusList.ItemsSource = _eng.Messages;
        RefreshProfileUi();
        ApplyWindowChrome();
        UpdateBackgroundSettings();

        Closing += (_, _) =>
        {
            _updateTimer?.Stop();
            _updateCancellation.Cancel();
            SaveNow();
        };
        StateChanged += (_, _) => UpdateMaximizeButton();

        // gentle fade-in when the window opens
        var root = (UIElement)Content;
        root.Opacity = 0;
        Loaded += (_, _) => root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    static List<StatusItem> DefaultStatuses() =>
        new() { new StatusItem { Text = "hello welcome to panda chatbox" } };

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

        UpdateButton.Content = "Checking for updates...";
        _ = CheckForUpdatesAsync();
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        _updateTimer.Start();

        RefreshUi();
    }

    void SaveNow() => SettingsStore.Save(_cfg, _eng.Messages);

    async Task CheckForUpdatesAsync()
    {
        if (_updateCheckInProgress || _updateCancellation.IsCancellationRequested) return;

        _updateCheckInProgress = true;
        UpdateButton.IsEnabled = false;
        UpdateButton.Foreground = (Brush)FindResource("Muted");
        UpdateButton.Content = "Checking for updates...";
        UpdateButton.ToolTip = null;
        _openReleasePageOnClick = false;

        try
        {
            var release = await ReleaseUpdateService.GetLatestReleaseAsync(_updateCancellation.Token);
            if (_updateCancellation.IsCancellationRequested) return;

            if (release == null)
            {
                _latestReleaseUri = new Uri("https://github.com/AstralShadows/Panda-Chatbox-OSC/releases");
                _updateAvailable = false;
                _openReleasePageOnClick = true;
                UpdateButton.Content = "No releases found · view releases";
                UpdateButton.ToolTip = "Open the GitHub releases page.";
                return;
            }

            _latestReleaseUri = release.PageUri;
            _latestReleaseVersion = release.Version;
            Version? currentVersion = GetCurrentVersion();
            _updateAvailable = !release.IsPrerelease
                && release.Version != null
                && currentVersion != null
                && release.Version > currentVersion;

            if (_updateAvailable)
            {
                _openReleasePageOnClick = true;
                UpdateButton.Content = $"Update available: v{release.Version} · click to view";
                UpdateButton.Foreground = (Brush)FindResource("Green");
                UpdateButton.ToolTip = "Open the GitHub release page to read the notes and download the update.";
                if (!_cfg.SuppressUpdateNotifications && !_updateNoticeShown)
                    QueueUpdateNotice();
            }
            else if (release.Version == null)
            {
                _openReleasePageOnClick = true;
                UpdateButton.Content = "View GitHub releases";
                UpdateButton.ToolTip = "The latest release has no version tag to compare. Click to view GitHub releases.";
            }
            else
            {
                UpdateButton.Content = $"Up to date · v{currentVersion}";
                UpdateButton.ToolTip = $"Latest stable release: v{release.Version}. Click to check again.";
            }
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (_updateCancellation.IsCancellationRequested) return;
            _updateAvailable = false;
            UpdateButton.Content = "Couldn't check for updates · click to retry";
            UpdateButton.ToolTip = exception.Message;
        }
        finally
        {
            _updateCheckInProgress = false;
            if (!_updateCancellation.IsCancellationRequested)
                UpdateButton.IsEnabled = true;
        }
    }

    static Version? GetCurrentVersion()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        return version == null ? null : new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));
    }

    async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_openReleasePageOnClick)
        {
            await CheckForUpdatesAsync();
        }
        if (!_openReleasePageOnClick || _latestReleaseUri == null) return;

        if (_updateAvailable)
        {
            if (_cfg.SuppressUpdateNotifications)
            {
                OpenReleasePage(_latestReleaseUri);
                return;
            }
            ShowUpdateNotice();
            return;
        }

        OpenReleasePage(_latestReleaseUri);
    }

    void RearrangeLines_Click(object sender, RoutedEventArgs e)
    {
        var keys = _cfg.DisplayLineOrder.Count > 0 ? _cfg.DisplayLineOrder.ToList() : AppSettings.DefaultDisplayLineOrder().ToList();
        var displayItems = keys.Select(ToDisplayName).ToList();
        var displayMap = keys.ToDictionary(key => ToDisplayName(key), key => key, StringComparer.OrdinalIgnoreCase);

        var dialog = new Window
        {
            Title = "Rearrange chatbox lines",
            Owner = this,
            Width = 320,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)FindResource("Bg")
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Drag the order below",
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Text")
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        var up = new Button { Content = "↑ Up", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var down = new Button { Content = "↓ Down", Width = 80 };
        actions.Children.Add(up);
        actions.Children.Add(down);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        var list = new ListBox { Height = 280, ItemsSource = displayItems, SelectedIndex = 0 };
        Grid.SetRow(list, 2);
        root.Children.Add(list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "Apply", Width = 90, Style = (Style)FindResource("Primary") };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        up.Click += (_, _) =>
        {
            int idx = list.SelectedIndex;
            if (idx <= 0) return;
            var items = displayItems.ToList();
            var sel = items[idx];
            items.RemoveAt(idx);
            items.Insert(idx - 1, sel);
            list.ItemsSource = items;
            list.SelectedIndex = idx - 1;
            displayItems.Clear();
            foreach (var item in items) displayItems.Add(item);
        };

        down.Click += (_, _) =>
        {
            int idx = list.SelectedIndex;
            if (idx < 0 || idx >= displayItems.Count - 1) return;
            var items = displayItems.ToList();
            var sel = items[idx];
            items.RemoveAt(idx);
            items.Insert(idx + 1, sel);
            list.ItemsSource = items;
            list.SelectedIndex = idx + 1;
            displayItems.Clear();
            foreach (var item in items) displayItems.Add(item);
        };

        cancel.Click += (_, _) => dialog.Close();
        apply.Click += (_, _) =>
        {
            var order = list.Items.Cast<string>().Select(item => displayMap[item]).ToList();
            _cfg.DisplayLineOrder = new ObservableCollection<string>(order);
            _cfg.EnsureDisplayLineOrder();
            RefreshUi();
            SaveNow();
            dialog.Close();
        };

        dialog.Content = root;
        dialog.ShowDialog();
    }

    static string ToDisplayName(string key) => key switch
    {
        "Status" => "Status",
        "CPU" => "CPU",
        "RAM" => "RAM",
        "GPU" => "GPU",
        "VRAM" => "VRAM",
        "DateTime" => "Date / Time",
        "Weather" => "Weather",
        "Music" => "Music",
        "App" => "App",
        "Battery" => "Battery",
        "Uptime" => "Uptime",
        _ => key
    };

    void ShowUpdateNotice()    {
        if (!IsVisible || !_updateAvailable || _latestReleaseUri == null || _latestReleaseVersion == null) return;
        var notice = new UpdateNoticeWindow(_latestReleaseVersion, GetCurrentVersion() ?? new Version(0, 0));
        notice.Owner = this;
        notice.ShowDialog();

        if (notice.DontShowAgain)
        {
            _cfg.SuppressUpdateNotifications = true;
            SaveNow();
        }
        if (notice.OpenReleaseRequested) OpenReleasePage(_latestReleaseUri);
    }

    void QueueUpdateNotice()
    {
        if (_updateNoticeShown || _cfg.SuppressUpdateNotifications) return;
        _updateNoticeShown = true;
        if (IsVisible)
            Dispatcher.BeginInvoke(new Action(ShowUpdateNotice), DispatcherPriority.ApplicationIdle);
        else
            Loaded += MainWindow_UpdateNoticeLoaded;
    }

    void MainWindow_UpdateNoticeLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_UpdateNoticeLoaded;
        Dispatcher.BeginInvoke(new Action(ShowUpdateNotice), DispatcherPriority.ApplicationIdle);
    }

    void OpenReleasePage(Uri releaseUri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(releaseUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Couldn't open the GitHub release page:\n\n" + exception.Message,
                "Panda Chatbox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
        ActiveProfileText.Text = string.IsNullOrWhiteSpace(_cfg.ManualProfileName)
            ? $"Automatic selection · {_eng.ActiveCollectionName}"
            : $"Manual profile · {_cfg.ManualProfileName} · {_eng.ActiveCollectionName}";

        CpuDetectedText.Text = _eng.Hardware.CpuName;
        RamDetectedText.Text = $"{_eng.Hardware.UsedRamGb:0.##}/{Math.Ceiling(_eng.Hardware.TotalRamGb):0} GB used";
        GpuDetectedText.Text = _eng.Hardware.GpuName;
        VramDetectedText.Text = _eng.Hardware.UsedVramGb is double usedVram
            ? $"{usedVram:0.##}/{Math.Ceiling(_eng.Hardware.TotalVramGb):0} GB used"
            : $"Usage unavailable / {Math.Ceiling(_eng.Hardware.TotalVramGb):0} GB";
        VramDetectedText.ToolTip = _eng.Hardware.VramUsageError.Length == 0
            ? "Dedicated GPU memory currently used by all apps / total capacity"
            : _eng.Hardware.VramUsageError;

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
        _eng.Messages.Add(new StatusItem { Text = t, Collection = StatusCollectionEdit.Text });
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

    void Collection_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) _eng.StatusChanged();
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

    void IntegrationToggle_Click(object sender, RoutedEventArgs e) => _eng.Kick();

    void Profiles_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!string.IsNullOrWhiteSpace(_cfg.ManualProfileName)
                && !_cfg.StatusProfiles.Any(profile => profile != null
                    && string.Equals(profile.Name, _cfg.ManualProfileName, StringComparison.OrdinalIgnoreCase)))
                _cfg.ManualProfileName = "";
            foreach (var profile in _cfg.StatusProfiles.Where(profile => profile != null))
            {
                profile.Name = profile.Name?.Trim() ?? "";
                profile.AppMatch = profile.AppMatch?.Trim() ?? "";
                profile.Collection = string.IsNullOrWhiteSpace(profile.Collection) ? "Default" : profile.Collection.Trim();
            }
            RefreshProfileUi();
            _eng.ConfigurationChanged();
            SaveNow();
        }), DispatcherPriority.Background);
    }

    void Schedules_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _eng.ConfigurationChanged();
            SaveNow();
        }), DispatcherPriority.Background);
    }

    void RefreshProfileUi()
    {
        if (ManualProfileCombo == null) return;
        _updatingProfileUi = true;
        var names = new List<string> { "Auto" };
        names.AddRange(_cfg.StatusProfiles
            .Where(profile => profile != null)
            .Select(profile => profile.Name?.Trim() ?? "")
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(_cfg.ManualProfileName)
            && !names.Any(name => string.Equals(name, _cfg.ManualProfileName, StringComparison.OrdinalIgnoreCase)))
            _cfg.ManualProfileName = "";
        ManualProfileCombo.ItemsSource = names;
        string selected = names.FirstOrDefault(name =>
            string.Equals(name, _cfg.ManualProfileName, StringComparison.OrdinalIgnoreCase)) ?? "Auto";
        ManualProfileCombo.SelectedItem = selected;
        _updatingProfileUi = false;
    }

    void ManualProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfileUi || !IsLoaded) return;
        _cfg.ManualProfileName = string.Equals(ManualProfileCombo.SelectedItem as string, "Auto", StringComparison.OrdinalIgnoreCase)
            ? ""
            : ManualProfileCombo.SelectedItem as string ?? "";
        _eng.ConfigurationChanged();
        SaveNow();
        RefreshUi();
    }

    void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (Keyboard.FocusedElement is TextBox or ComboBox or PasswordBox) return;

        string hotkey = NormalizeHotkey(Keyboard.Modifiers, e.Key);
        if (string.IsNullOrWhiteSpace(hotkey)) return;

        var preset = _cfg.QuickStatusPresets.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase));
        if (preset == null) return;

        ApplyQuickPreset(preset);
        e.Handled = true;
    }

    static string NormalizeHotkey(ModifierKeys modifiers, Key key)
    {
        var pieces = new List<string>();
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) pieces.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) pieces.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) pieces.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows) pieces.Add("Win");

        if (key == Key.None) return "";
        string keyText = key.ToString();
        if (keyText.StartsWith("D") && keyText.Length > 1 && char.IsDigit(keyText[1])) keyText = keyText[1..];
        if (keyText.StartsWith("NumPad")) keyText = keyText[6..];
        if (pieces.Count == 0) return keyText;
        return string.Join("+", pieces) + "+" + keyText;
    }

    static bool TryParseHotkey(string? hotkey, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        string[] parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (string part in parts)
        {
            if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Control;
            else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Alt;
            else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Shift;
            else if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Windows;
            else if (Enum.TryParse<Key>(part, true, out var parsedKey)) key = parsedKey;
            else if (part.Length == 1 && char.IsDigit(part[0]))
                key = part[0] switch
                {
                    '0' => Key.D0,
                    '1' => Key.D1,
                    '2' => Key.D2,
                    '3' => Key.D3,
                    '4' => Key.D4,
                    '5' => Key.D5,
                    '6' => Key.D6,
                    '7' => Key.D7,
                    '8' => Key.D8,
                    '9' => Key.D9,
                    _ => Key.None
                };
            else if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[1..], out var funcKey) && funcKey >= 1 && funcKey <= 12)
                key = (Key)Enum.Parse(typeof(Key), "F" + funcKey, true);
            else return false;

            if (key == Key.None && part.Length == 1 && char.IsDigit(part[0])) return false;
        }

        return key != Key.None;
    }

    void ApplyQuickPreset(QuickStatusPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Collection)) preset.Collection = "Default";
        if (!string.IsNullOrWhiteSpace(preset.Collection))
            _eng.SetCollection(preset.Collection);

        if (!string.IsNullOrWhiteSpace(preset.Message))
            _eng.SendChat(preset.Message.Trim());
        else
            _eng.Kick();

        SaveNow();
    }

    void AddQuickPreset_Click(object sender, RoutedEventArgs e)
    {
        string name = QuickPresetNameEdit.Text.Trim();
        string message = QuickPresetMessageEdit.Text.Trim();
        string collection = QuickPresetCollectionEdit.Text.Trim();
        string hotkey = QuickPresetHotkeyEdit.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Give the quick preset a name first.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(hotkey) || !TryParseHotkey(hotkey, out var modifiers, out var key))
        {
            MessageBox.Show(this, "Use a shortcut like Ctrl+Alt+1 or F5.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_cfg.QuickStatusPresets.Any(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A quick preset with that name already exists.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var normalized = NormalizeHotkey(modifiers, key);
        _cfg.QuickStatusPresets.Add(new QuickStatusPreset
        {
            Name = name,
            Message = message,
            Collection = string.IsNullOrWhiteSpace(collection) ? "Default" : collection,
            Hotkey = normalized,
            Enabled = true
        });

        QuickPresetNameEdit.Clear();
        QuickPresetMessageEdit.Clear();
        QuickPresetCollectionEdit.Text = "Default";
        QuickPresetHotkeyEdit.Clear();
        SaveNow();
    }

    void RemoveQuickPreset_Click(object sender, RoutedEventArgs e)
    {
        QuickPresetsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        QuickPresetsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (QuickPresetsGrid.SelectedItem is not QuickStatusPreset preset) return;
        _cfg.QuickStatusPresets.Remove(preset);
        SaveNow();
    }

    void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileNameEdit.Text.Trim();
        string collection = ProfileCollectionEdit.Text.Trim();
        if (name.Length == 0 || collection.Length == 0)
        {
            MessageBox.Show(this, "Enter both a profile name and a collection name.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.Equals(name, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "'Auto' is reserved for automatic profile selection.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_cfg.StatusProfiles.Any(profile => profile != null
            && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A profile with that name already exists.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cfg.StatusProfiles.Add(new StatusProfile
        {
            Name = name,
            AppMatch = ProfileAppEdit.Text.Trim(),
            Collection = collection
        });
        ProfileNameEdit.Clear();
        ProfileAppEdit.Clear();
        RefreshProfileUi();
        _eng.ConfigurationChanged();
        SaveNow();
    }

    void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfilesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ProfilesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (ProfilesGrid.SelectedItem is not StatusProfile profile) return;
        _cfg.StatusProfiles.Remove(profile);
        if (string.Equals(_cfg.ManualProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
            _cfg.ManualProfileName = "";
        RefreshProfileUi();
        _eng.ConfigurationChanged();
        SaveNow();
    }

    void AddSchedule_Click(object sender, RoutedEventArgs e)
    {
        string name = ScheduleNameEdit.Text.Trim();
        string collection = ScheduleCollectionEdit.Text.Trim();
        bool validStart = TimeOnly.TryParseExact(ScheduleStartEdit.Text.Trim(), "HH:mm",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
        bool validEnd = TimeOnly.TryParseExact(ScheduleEndEdit.Text.Trim(), "HH:mm",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
        if (name.Length == 0 || collection.Length == 0 || !validStart || !validEnd)
        {
            MessageBox.Show(this, "Enter a schedule name and collection, with start and end times in 24-hour HH:mm format.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.Equals(ScheduleStartEdit.Text.Trim(), ScheduleEndEdit.Text.Trim(), StringComparison.Ordinal))
        {
            MessageBox.Show(this, "The start and end times must be different.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cfg.ScheduledProfiles.Add(new ScheduledStatusProfile
        {
            Name = name,
            StartTime = ScheduleStartEdit.Text.Trim(),
            EndTime = ScheduleEndEdit.Text.Trim(),
            Collection = collection
        });
        ScheduleNameEdit.Clear();
        _eng.ConfigurationChanged();
        SaveNow();
    }

    void RemoveSchedule_Click(object sender, RoutedEventArgs e)
    {
        SchedulesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SchedulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (SchedulesGrid.SelectedItem is not ScheduledStatusProfile profile) return;
        _cfg.ScheduledProfiles.Remove(profile);
        _eng.ConfigurationChanged();
        SaveNow();
    }

    void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Panda Chatbox settings",
            Filter = "Panda Chatbox backup (*.json)|*.json",
            DefaultExt = ".json",
            FileName = "Panda-Chatbox-Settings.json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SettingsStore.Export(dialog.FileName, _cfg, _eng.Messages);
            MessageBox.Show(this, "Settings and statuses were exported successfully.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, "Settings could not be exported:\n\n" + exception.Message, "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Panda Chatbox settings",
            Filter = "Panda Chatbox backup (*.json)|*.json|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "Importing replaces your current settings and status messages. Continue?",
                "Panda Chatbox", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        AppSettings? previousSettings = null;
        List<StatusItem>? previousItems = null;
        bool importStarted = false;
        try
        {
            previousSettings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_cfg))
                ?? throw new InvalidDataException("The current settings could not be backed up before import.");
            previousItems = _eng.Messages.ToList();
            SaveFile backup = SettingsStore.Import(dialog.FileName);
            backup.Settings.StatusProfiles ??= new ObservableCollection<StatusProfile>();
            backup.Settings.ScheduledProfiles ??= new ObservableCollection<ScheduledStatusProfile>();
            backup.Settings.ManualProfileName ??= "";
            backup.Items ??= new List<StatusItem>();
            ValidateImport(backup);
            ThemeManager.Apply(backup.Settings);
            importStarted = true;
            _cfg.CopyFrom(backup.Settings);
            _eng.Messages.Clear();
            foreach (var message in backup.Items) _eng.Messages.Add(message);
            DataContext = null;
            DataContext = _cfg;
            TextColorBox.Text = _cfg.TextColor;
            ApplyWindowChrome();
            UpdateBackgroundSettings();
            RefreshProfileUi();
            _eng.ApplyConnection();
            _eng.ConfigurationChanged();
            SaveNow();
            RefreshUi();
            MessageBox.Show(this, "Settings and statuses were imported successfully.", "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                           or JsonException or ArgumentException or InvalidDataException
                                           or NotSupportedException or FormatException or InvalidOperationException)
        {
            string errorText = exception.Message;
            if (importStarted && previousSettings != null && previousItems != null)
            {
                _cfg.CopyFrom(previousSettings);
                _eng.Messages.Clear();
                foreach (var message in previousItems) _eng.Messages.Add(message);
                DataContext = null;
                DataContext = _cfg;
                TextColorBox.Text = _cfg.TextColor;
                ApplyWindowChrome();
                UpdateBackgroundSettings();
                RefreshProfileUi();
                _eng.ApplyConnection();
                _eng.ConfigurationChanged();
                SaveNow();
            }
            try
            {
                ThemeManager.Apply(_cfg);
            }
            catch (Exception restoreException) when (restoreException is IOException or ArgumentException
                                                     or NotSupportedException or FormatException or InvalidOperationException)
            {
                errorText = new AggregateException(exception, restoreException).Message;
            }
            MessageBox.Show(this, "Settings could not be imported:\n\n" + errorText, "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    static void ValidateImport(SaveFile backup)
    {
        if (backup.Items!.Any(item => item == null))
            throw new InvalidDataException("The backup contains an empty status entry.");

        var profiles = backup.Settings.StatusProfiles!;
        if (profiles.Any(profile => profile == null
                                    || string.IsNullOrWhiteSpace(profile.Name)
                                    || string.Equals(profile.Name.Trim(), "Auto", StringComparison.OrdinalIgnoreCase)
                                    || string.IsNullOrWhiteSpace(profile.Collection)))
            throw new InvalidDataException("Every profile needs a unique name and a collection.");
        if (profiles.Select(profile => profile.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profiles.Count)
            throw new InvalidDataException("Profile names must be unique.");

        foreach (var schedule in backup.Settings.ScheduledProfiles!)
        {
            if (schedule == null)
                throw new InvalidDataException("The backup contains an empty schedule entry.");
            bool validStart = TimeOnly.TryParseExact(schedule.StartTime, "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start);
            bool validEnd = TimeOnly.TryParseExact(schedule.EndTime, "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var end);
            if (string.IsNullOrWhiteSpace(schedule.Name)
                || string.IsNullOrWhiteSpace(schedule.Collection) || !validStart || !validEnd || start == end)
                throw new InvalidDataException("Each schedule needs a name, collection, and distinct 24-hour HH:mm start/end times.");
        }

        if (!string.IsNullOrWhiteSpace(backup.Settings.ManualProfileName)
            && !profiles.Any(profile => string.Equals(profile.Name, backup.Settings.ManualProfileName, StringComparison.OrdinalIgnoreCase)))
            backup.Settings.ManualProfileName = "";
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
