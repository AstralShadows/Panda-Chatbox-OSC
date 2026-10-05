using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PandaChatbox;

/// <summary>
/// The brains: samples PC hardware, rotates status messages, builds the chatbox text and sends it.
/// Everything runs on the UI thread (Tick is called from a DispatcherTimer), so no locking is needed.
/// </summary>
public sealed class Engine
{
    public const int ChatboxLimit = 144;

    static readonly string[] DateFormats = { "ddd, MMM d", "M/d", "d/M", "MMM d, yyyy", "yyyy-MM-dd" };

    public readonly AppSettings Cfg;
    public readonly ObservableCollection<StatusItem> Messages;
    public readonly OscClient Osc = new();
    public readonly WeatherService Weather = new();
    internal PcHardwareMonitor Hardware => _hardware;
    public string ActiveCollectionName => _activeCollection ?? "All collections";

    // latest measured hardware usage
    StatusItem? _current;
    readonly PcHardwareMonitor _hardware = new();
    double _cpu, _ram;
    string? _activeCollection;
    string _foregroundApp = "", _foregroundExecutable = "", _foregroundTitle = "";

    // chat override
    string _overrideText = "";
    double _overrideUntil;
    double _lastChatSend;

    // integrations state
    string _activeApp = "", _mediaApp = "", _mediaText = "";
    readonly MediaService _media = new();
    bool _mediaBusy, _weatherBusy, _weatherDirty = true;
    double _weatherDirtyAt, _lastWeather, _lastScan;
    string _wCity;
    bool _wFahrenheit, _wShown;

    // scheduling
    double _lastMsg, _lastHw, _lastSend;
    bool _kick, _force;
    string _lastTemplateClock = "";
    readonly Random _rng = new();

    static double Now => Environment.TickCount64 / 1000.0;
    public Engine(AppSettings cfg, ObservableCollection<StatusItem> messages)
    {
        Cfg = cfg;
        Cfg.StatusProfiles ??= new ObservableCollection<StatusProfile>();
        Cfg.ScheduledProfiles ??= new ObservableCollection<ScheduledStatusProfile>();
        Cfg.QuickStatusPresets ??= new ObservableCollection<QuickStatusPreset>();
        Cfg.DisplayLineOrder ??= new ObservableCollection<string>();
        Cfg.EnsureDisplayLineOrder();
        Cfg.ManualProfileName ??= "";
        Messages = messages;
        _wCity = cfg.City;
        _wFahrenheit = cfg.Fahrenheit;
        _wShown = cfg.ShowWeather;
    }

    public void Start()
    {
        ApplyConnection();
        SampleHardware();
        _lastHw = Now;
        PickStatus();
    }

    // ---------- public actions used by the UI ----------
    public void ApplyConnection() => Osc.Configure(Cfg.Ip, Cfg.Port);
    public void Kick() => _kick = true;
    public void SendNow() => SendText(Compose());
    public void SendClear() => Osc.Send("", true, false);
    public void SendChat(string text)
    {
        _overrideText = text;
        _overrideUntil = Now + Cfg.ChatDuration;
        _kick = true;
    }
    public void StopChat()
    {
        _overrideUntil = 0;
        _kick = true;
    }
    public string ConnectionError => Osc.Error;
    public string WeatherStatus => Cfg.ShowWeather ? "Now: " + Weather.Status : "Weather is off";
    public string MediaStatus =>
        !Cfg.ShowMedia ? "Now playing is off" :
        _mediaText.Length == 0 ? "Detected: nothing playing" :
        $"Detected: {_mediaApp} - {_mediaText}";

    void SampleHardware()
    {
        _hardware.Sample();
        _cpu = _hardware.CpuUsagePercent;
        _ram = _hardware.UsedRamGb;
    }

    void SendText(string t)
    {
        if (Cfg.TrimToLimit) t = NativeInterop.TrimChatText(t, ChatboxLimit);
        _lastSend = Now;
        Osc.Send(t, Cfg.Immediate, Cfg.Sound);
    }

    // ---------- status messages ----------
    static bool Usable(StatusItem m) => m.Enabled && m.Text.Trim().Length > 0;

    /// <summary>Moves to the next status according to the rotation mode (in order / random / favorites only).</summary>
    public void PickStatus()
    {
        var flags = Messages.Select(message =>
            (message.Enabled ? 1 : 0) |
            (message.Text.Trim().Length > 0 ? 2 : 0) |
            (message.Favorite ? 4 : 0)).ToArray();
        var collections = Messages.Select(message => message.Collection).ToArray();
        var currentIndex = _current is null ? -1 : Messages.IndexOf(_current);
        var nextIndex = NativeInterop.PickStatus(Cfg.StatusMode, flags, collections, _activeCollection, currentIndex, _rng.Next());
        StatusItem? next = nextIndex < 0 ? null : Messages[nextIndex];
        SetCurrent(next);
    }

    void SetCurrent(StatusItem? n)
    {
        if (_current != null) _current.IsCurrent = false;
        _current = n;
        if (n != null) n.IsCurrent = true;
    }

    bool CurrentValid()
    {
        if (_current is null || !Usable(_current)
            || (_activeCollection != null && !string.Equals(_current.Collection, _activeCollection, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (Cfg.StatusMode != 2 || _current.Favorite)
            return true;
        return !Messages.Any(message => Usable(message) && message.Favorite
            && (_activeCollection == null || string.Equals(message.Collection, _activeCollection, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Call after the list or a row changes (delete, power, heart, edit, mode change).</summary>
    public void StatusChanged()
    {
        if (!CurrentValid()) PickStatus();
        _force = true;
    }

    string StatusLine()
    {
        if (_current == null) return "[No Status Active]";
        string text = StatusTemplate.Expand(_current.Text, TemplateValues());
        return Cfg.StatusPrefix + StatusStyles.Apply(_current.StyleIndex, text) + Cfg.StatusSuffix;
    }

    Dictionary<string, string> TemplateValues()
    {
        var now = DateTime.Now;
        string timeFormat = Cfg.H24
            ? (Cfg.ShowSeconds ? "HH:mm:ss" : "HH:mm")
            : (Cfg.ShowSeconds ? "h:mm:ss tt" : "h:mm tt");
        string ram = $"{StatusTemplate.FormatBytes(_ram)}/{Math.Ceiling(_hardware.TotalRamGb):0}GB";
        string vram = _hardware.UsedVramGb is double usedVram
            ? $"{StatusTemplate.FormatBytes(usedVram)}/{Math.Ceiling(_hardware.TotalVramGb):0}GB"
            : "Usage unavailable";
        string battery = Native.GetSystemPowerStatus(out var power) && power.BatteryFlag != 128 && power.BatteryLifePercent <= 100
            ? $"{power.BatteryLifePercent}%" + (power.ACLineStatus == 1 ? " (charging)" : "")
            : "N/A";
        long uptimeSeconds = Environment.TickCount64 / 1000;
        int days = (int)(uptimeSeconds / 86400), hours = (int)(uptimeSeconds / 3600 % 24), minutes = (int)(uptimeSeconds / 60 % 60);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["time"] = now.ToString(timeFormat, CultureInfo.InvariantCulture),
            ["date"] = now.ToString(DateFormats[Math.Clamp(Cfg.DateStyle, 0, DateFormats.Length - 1)], CultureInfo.InvariantCulture),
            ["app"] = _foregroundApp,
            ["game"] = _foregroundApp,
            ["song"] = _mediaText.Length == 0 ? "" : Cfg.ShowMediaApp ? $"{_mediaText} ({_mediaApp})" : _mediaText,
            ["player"] = _mediaApp,
            ["cpu"] = $"{_cpu:0}%",
            ["ram"] = ram,
            ["vram"] = vram,
            ["battery"] = battery,
            ["uptime"] = days > 0 ? $"{days}d {hours}h" : $"{hours}h {minutes}m"
        };
    }

    // ---------- main tick (every ~50 ms) ----------
    public void Tick()
    {
        double now = Now;
        UpdateIntegrations(now);
        UpdateTemplateClock();

        if (now < _overrideUntil)   // chat mode: ONLY the typed message is sent
        {
            if (_kick || now - _lastChatSend >= 1.0)
            {
                _kick = false;
                _lastChatSend = now;
                SendText(_overrideText);
            }
            _force = true;
            return;
        }

        void UpdateTemplateClock()
        {
            string key = "";
            if (_current != null && Cfg.ShowStatus)
            {
                string text = _current.Text;
                var localNow = DateTime.Now;
                if (text.Contains("{time}", StringComparison.OrdinalIgnoreCase))
                {
                    string format = Cfg.H24
                        ? (Cfg.ShowSeconds ? "HH:mm:ss" : "HH:mm")
                        : (Cfg.ShowSeconds ? "h:mm:ss tt" : "h:mm tt");
                    key += localNow.ToString(format, CultureInfo.InvariantCulture);
                }
                if (text.Contains("{date}", StringComparison.OrdinalIgnoreCase))
                    key += "|" + localNow.ToString(DateFormats[Math.Clamp(Cfg.DateStyle, 0, DateFormats.Length - 1)], CultureInfo.InvariantCulture);
            }

            if (!string.Equals(key, _lastTemplateClock, StringComparison.Ordinal))
            {
                _lastTemplateClock = key;
                if (key.Length > 0) _force = true;
            }
        }

        bool send = _force;
        _force = false;
        if (now - _lastHw >= Cfg.HardwareUpdateInterval)
        {
            SampleHardware();
            _lastHw = now;
            send = true;
        }
        if (now - _lastMsg >= Cfg.MessageDelay) { PickStatus(); _lastMsg = now; send = true; }
        if (_kick) { _kick = false; send = true; }
        if (send && Cfg.Send)
        {
            if (now - _lastSend >= Cfg.MinSendInterval) SendText(Compose());
            else _force = true;   // too soon after the last send: try again on a later tick
        }
    }

    // ---------- integrations ----------
    void UpdateIntegrations(double now)
    {
        if (now - _lastScan >= 2.0)
        {
            _lastScan = now;
            bool mediaNeededForTemplate = Messages.Any(message => Usable(message)
                && (message.Text.Contains("{song}", StringComparison.OrdinalIgnoreCase)
                    || message.Text.Contains("{player}", StringComparison.OrdinalIgnoreCase)));
            if (Cfg.ShowMedia || mediaNeededForTemplate) _ = ScanMediaAsync();
            else { _mediaApp = ""; _mediaText = ""; }
            var foreground = Desktop.ForegroundWindow();
            string oldForegroundApp = _foregroundApp;
            _foregroundApp = foreground?.AppName ?? "";
            _foregroundExecutable = foreground?.Executable ?? "";
            _foregroundTitle = foreground?.Title ?? "";
            if (_foregroundApp.Length > 0) _activeApp = _foregroundApp;
            if (!string.Equals(oldForegroundApp, _foregroundApp, StringComparison.Ordinal)
                && Messages.Any(message => Usable(message)
                    && (message.Text.Contains("{app}", StringComparison.OrdinalIgnoreCase)
                        || message.Text.Contains("{game}", StringComparison.OrdinalIgnoreCase))))
                _kick = true;
            UpdateActiveCollection(now);
        }

        // weather: refetch when the city/units change (debounced while typing) or the toggle is switched on
        if (Cfg.City != _wCity || Cfg.Fahrenheit != _wFahrenheit)
        {
            _wCity = Cfg.City;
            _wFahrenheit = Cfg.Fahrenheit;
            _weatherDirty = true;
            _weatherDirtyAt = now;
        }

        if (Cfg.ShowWeather != _wShown)
        {
            _wShown = Cfg.ShowWeather;
            if (_wShown) { _weatherDirty = true; _weatherDirtyAt = now - 2.0; }
        }
        if (Cfg.ShowWeather && !_weatherBusy)
        {
            bool due = _weatherDirty ? now - _weatherDirtyAt >= 1.5 : now - _lastWeather >= Cfg.WeatherMinutes * 60.0;
            if (due)
            {
                _weatherDirty = false;
                _ = FetchWeatherAsync();
            }
        }
    }

    void UpdateActiveCollection(double now)
    {
        string? collection = null;
        var manual = Cfg.StatusProfiles.FirstOrDefault(profile =>
            profile != null && !string.IsNullOrWhiteSpace(Cfg.ManualProfileName)
            && string.Equals(profile.Name, Cfg.ManualProfileName, StringComparison.OrdinalIgnoreCase));
        if (manual != null)
            collection = manual.Collection;
        else
        {
            var scheduled = Cfg.ScheduledProfiles
                .Select((profile, index) => (Profile: profile, Index: index))
                .Where(item => item.Profile != null && IsScheduleActive(item.Profile, DateTime.Now))
                .OrderByDescending(item => item.Profile.Priority)
                .ThenBy(item => item.Index)
                .Select(item => item.Profile)
                .FirstOrDefault();
            collection = scheduled?.Collection;
            if (scheduled == null)
            {
                collection = Cfg.StatusProfiles
                    .Select((profile, index) => (Profile: profile, Index: index))
                    .Where(item => item.Profile != null && !string.IsNullOrWhiteSpace(item.Profile.AppMatch)
                        && (_foregroundApp.Contains(item.Profile.AppMatch, StringComparison.OrdinalIgnoreCase)
                            || _foregroundExecutable.Contains(item.Profile.AppMatch, StringComparison.OrdinalIgnoreCase)
                            || _foregroundTitle.Contains(item.Profile.AppMatch, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(item => item.Profile.Priority)
                    .ThenByDescending(item => item.Profile.AppMatch.Length)
                    .ThenBy(item => item.Index)
                    .Select(item => item.Profile.Collection)
                    .FirstOrDefault();
            }
        }

        collection = string.IsNullOrWhiteSpace(collection) ? null : collection.Trim();
        if (string.Equals(collection, _activeCollection, StringComparison.OrdinalIgnoreCase)) return;
        _activeCollection = collection;
        PickStatus();
        _lastMsg = now;
        _force = true;
    }

    public void ConfigurationChanged()
    {
        UpdateActiveCollection(Now);
        _force = true;
    }

    public void SetCollection(string collectionName)
    {
        _activeCollection = string.IsNullOrWhiteSpace(collectionName) ? null : collectionName.Trim();
        PickStatus();
        _lastMsg = Now;
        _force = true;
    }

    static bool IsScheduleActive(ScheduledStatusProfile profile, DateTime now)
    {
        if (!profile.Enabled
            || !TimeOnly.TryParseExact(profile.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(profile.EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            || start == end)
            return false;

        var current = TimeOnly.FromDateTime(now);
        if (start < end)
            return IsScheduleDayActive(profile.Days, now.DayOfWeek) && current >= start && current < end;
        if (current >= start)
            return IsScheduleDayActive(profile.Days, now.DayOfWeek);
        if (current < end)
            return IsScheduleDayActive(profile.Days, now.AddDays(-1).DayOfWeek);
        return false;
    }

    static bool IsScheduleDayActive(string days, DayOfWeek day)
    {
        if (string.IsNullOrWhiteSpace(days)) return false;
        var tokens = days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Any(token => !Enum.GetNames<DayOfWeek>()
            .Any(name => name.StartsWith(token, StringComparison.OrdinalIgnoreCase) && token.Length >= 3)))
            return false;
        string abbreviation = day.ToString()[..3];
        return tokens.Any(item => item.Equals(abbreviation, StringComparison.OrdinalIgnoreCase)
            || item.Equals(day.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    async Task ScanMediaAsync()
    {
        if (_mediaBusy) return;
        _mediaBusy = true;
        try
        {
            var (app, text) = await _media.ScanAsync(Cfg.MediaSpotify, Cfg.MediaYoutube, Cfg.MediaOther);
            if (app != _mediaApp || text != _mediaText)
            {
                _mediaApp = app;
                _mediaText = text;
                _kick = true;
            }
        }
        catch { /* ignore */ }
        finally { _mediaBusy = false; }
    }

    async Task FetchWeatherAsync()
    {
        _weatherBusy = true;
        try
        {
            string before = Weather.Text;
            bool ok = await Weather.FetchAsync(Cfg.City, Cfg.Fahrenheit);
            if (ok && Weather.Text != before) _kick = true;
            // on failure retry in about a minute
            _lastWeather = ok ? Now : Now - Math.Max(0.0, Cfg.WeatherMinutes * 60.0 - 60.0);
        }
        finally { _weatherBusy = false; }
    }

    // ---------- build the chatbox text ----------
    static string Tagged(string tag, string body) => tag.Length == 0 ? body : tag + " " + body;
    static string UsedGb(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string WholeGb(double v) => Math.Ceiling(v).ToString("0", CultureInfo.InvariantCulture);

    public string Compose()
    {
        if (Now < _overrideUntil) return _overrideText;

        string sep = Cfg.StatSeparator;
        var entries = new List<(string Key, string Text)>();
        if (Cfg.ShowStatus)
        {
            string s = StatusLine();
            if (s.Length > 0) entries.Add(("Status", s));
        }
        entries.AddRange(BuildIntegrationEntries(sep));

        if (Cfg.ShowCpu)
            entries.Add(("CPU", Tagged(Cfg.TagCpu, Cfg.ShowCpuName ? $"{_hardware.CpuName}{sep}{_cpu:0}%" : $"{_cpu:0}%")));
        if (Cfg.ShowRam)
        {
            string pct = Cfg.ShowRamPercent ? $" ({(int)(_ram / _hardware.TotalRamGb * 100)}%)" : "";
            entries.Add(("RAM", Tagged(Cfg.TagRam, $"{UsedGb(_ram)}/{WholeGb(_hardware.TotalRamGb)}GB{pct}")));
        }
        if (Cfg.ShowGpu) entries.Add(("GPU", Tagged(Cfg.TagGpu, _hardware.GpuName)));
        if (Cfg.ShowVram)
        {
            string usage = _hardware.UsedVramGb is double used
                ? $"{UsedGb(used)}/{WholeGb(_hardware.TotalVramGb)}GB"
                : $"Usage unavailable / {WholeGb(_hardware.TotalVramGb)}GB";
            entries.Add(("VRAM", Tagged(Cfg.TagVram, usage)));
        }

        var order = (Cfg.DisplayLineOrder?.Count > 0 ? Cfg.DisplayLineOrder : AppSettings.DefaultDisplayLineOrder()).ToList();
        return NativeInterop.ComposeLines(entries.ToArray(), order.ToArray());
    }

    IEnumerable<(string Key, string Text)> BuildIntegrationEntries(string sep)
    {
        if (Cfg.ShowTime || Cfg.ShowDate)
        {
            var dt = DateTime.Now;
            string tl = "";
            if (Cfg.ShowDate) tl = dt.ToString(DateFormats[Math.Clamp(Cfg.DateStyle, 0, DateFormats.Length - 1)]);
            if (Cfg.ShowTime)
            {
                if (tl.Length > 0) tl += " | ";
                string fmt = Cfg.H24
                    ? (Cfg.ShowSeconds ? "HH:mm:ss" : "HH:mm")
                    : (Cfg.ShowSeconds ? "h:mm:ss tt" : "h:mm tt");
                tl += dt.ToString(fmt, CultureInfo.InvariantCulture);
            }
            yield return ("DateTime", Tagged(Cfg.ShowTime ? Cfg.TagTime : Cfg.TagDate, tl));
        }
        if (Cfg.ShowWeather && Weather.Text.Length > 0)
            yield return ("Weather", Tagged(Cfg.TagWeather, Weather.Text));
        if (Cfg.ShowMedia && _mediaText.Length > 0)
        {
            string t = _mediaText;
            int mx = Math.Max(10, Cfg.MediaMax);
            if (t.Length > mx) t = t[..(mx - 2)] + "..";
            yield return ("Music", Tagged(Cfg.TagMusic, Cfg.ShowMediaApp ? $"{t} ({_mediaApp})" : t));
        }
        if (Cfg.ShowApp && _activeApp.Length > 0)
            yield return ("App", Tagged(Cfg.TagApp, _activeApp));
        if (Cfg.ShowBattery && Native.GetSystemPowerStatus(out var sp) && sp.BatteryFlag != 128 && sp.BatteryLifePercent <= 100)
            yield return ("Battery", Tagged(Cfg.TagBattery, $"{sp.BatteryLifePercent}%" + (sp.ACLineStatus == 1 ? " (charging)" : "")));
        if (Cfg.ShowUptime)
        {
            long sec = Environment.TickCount64 / 1000;
            int d = (int)(sec / 86400), h = (int)(sec / 3600 % 24), m = (int)(sec / 60 % 60);
            yield return ("Uptime", Tagged(Cfg.TagUptime, d > 0 ? $"{d}d {h}h" : $"{h}h {m}m"));
        }
    }
}
