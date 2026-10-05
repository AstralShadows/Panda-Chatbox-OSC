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
    bool _openReleasePageOnClick;
    Uri? _latestReleaseUri;
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

        try
        {
            Process.Start(new ProcessStartInfo(_latestReleaseUri!.AbsoluteUri) { UseShellExecute = true });
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
