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

    // latest measured hardware usage
    StatusItem? _current;
    readonly PcHardwareMonitor _hardware = new();
    double _cpu, _ram;

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
    readonly Random _rng = new();

    static double Now => Environment.TickCount64 / 1000.0;

    public Engine(AppSettings cfg, ObservableCollection<StatusItem> messages)
    {
        Cfg = cfg;
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
        if (Cfg.TrimToLimit) t = Trim(t);
        _lastSend = Now;
        Osc.Send(t, Cfg.Immediate, Cfg.Sound);
    }

    static string Trim(string t)
    {
        if (t.Length <= ChatboxLimit) return t;
        int n = ChatboxLimit;
        if (char.IsHighSurrogate(t[n - 1])) n--;   // don't cut an emoji in half
        return t[..n];
    }

    // ---------- status messages ----------
    static bool Usable(StatusItem m) => m.Enabled && m.Text.Trim().Length > 0;

    /// <summary>Moves to the next status according to the rotation mode (in order / random / favorites only).</summary>
    public void PickStatus()
    {
        var pool = Messages.Where(Usable).ToList();
        if (Cfg.StatusMode == 2)
        {
            var fav = pool.Where(m => m.Favorite).ToList();
            if (fav.Count > 0) pool = fav;   // no favorites yet? fall back to everything
        }

        StatusItem? next = null;
        if (pool.Count > 0)
        {
            if (Cfg.StatusMode == 1 && pool.Count > 1)
            {
                do next = pool[_rng.Next(pool.Count)]; while (next == _current);
            }
            else
            {
                // in order: first usable message after the current one (wraps around)
                int start = _current == null ? -1 : Messages.IndexOf(_current);
                for (int k = 1; k <= Messages.Count && next == null; k++)
                {
                    var cand = Messages[(start + k) % Messages.Count];
                    if (pool.Contains(cand)) next = cand;
                }
                next ??= pool[0];
            }
        }
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
        var c = _current;
        if (c == null || !Messages.Contains(c) || !Usable(c)) return false;
        if (Cfg.StatusMode == 2 && !c.Favorite && Messages.Any(m => Usable(m) && m.Favorite)) return false;
        return true;
    }

    /// <summary>Call after the list or a row changes (delete, power, heart, edit, mode change).</summary>
    public void StatusChanged()
    {
        if (CurrentValid()) return;
        PickStatus();
        _force = true;
    }

    string StatusLine()
    {
        if (_current == null) return "[No Status Active]";
        return Cfg.StatusPrefix + StatusStyles.Apply(_current.StyleIndex, _current.Text) + Cfg.StatusSuffix;
    }

    // ---------- main tick (every ~50 ms) ----------
    public void Tick()
    {
        double now = Now;
        UpdateIntegrations(now);

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
            if (Cfg.ShowMedia) _ = ScanMediaAsync();
            else { _mediaApp = ""; _mediaText = ""; }
            if (Cfg.ShowApp)
            {
                string? app = Desktop.ForegroundApp();
                if (app != null) _activeApp = app;
            }
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
        var lines = new List<string>();
        if (Cfg.ShowStatus)
        {
            string s = StatusLine();
            if (s.Length > 0) lines.Add(s);
        }
        AddIntegrationLines(lines);

        if (Cfg.ShowCpu)
            lines.Add(Tagged(Cfg.TagCpu, Cfg.ShowCpuName ? $"{_hardware.CpuName}{sep}{_cpu:0}%" : $"{_cpu:0}%"));
        if (Cfg.ShowRam)
        {
            string pct = Cfg.ShowRamPercent ? $" ({(int)(_ram / _hardware.TotalRamGb * 100)}%)" : "";
            lines.Add(Tagged(Cfg.TagRam, $"{UsedGb(_ram)}/{WholeGb(_hardware.TotalRamGb)}GB{pct}"));
        }
        if (Cfg.ShowGpu) lines.Add(Tagged(Cfg.TagGpu, _hardware.GpuName));
        if (Cfg.ShowVram)
        {
            string usage = _hardware.UsedVramGb is double used
                ? $"{UsedGb(used)}/{WholeGb(_hardware.TotalVramGb)}GB"
                : $"Usage unavailable / {WholeGb(_hardware.TotalVramGb)}GB";
            lines.Add(Tagged(Cfg.TagVram, usage));
        }
        return string.Join("\n", lines);
    }

    void AddIntegrationLines(List<string> lines)
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
            lines.Add(Tagged(Cfg.ShowTime ? Cfg.TagTime : Cfg.TagDate, tl));
        }
        if (Cfg.ShowWeather && Weather.Text.Length > 0)
            lines.Add(Tagged(Cfg.TagWeather, Weather.Text));
        if (Cfg.ShowMedia && _mediaText.Length > 0)
        {
            string t = _mediaText;
            int mx = Math.Max(10, Cfg.MediaMax);
            if (t.Length > mx) t = t[..(mx - 2)] + "..";
            lines.Add(Tagged(Cfg.TagMusic, Cfg.ShowMediaApp ? $"{t} ({_mediaApp})" : t));
        }
        if (Cfg.ShowApp && _activeApp.Length > 0)
            lines.Add(Tagged(Cfg.TagApp, _activeApp));
        if (Cfg.ShowBattery && Native.GetSystemPowerStatus(out var sp) && sp.BatteryFlag != 128 && sp.BatteryLifePercent <= 100)
            lines.Add(Tagged(Cfg.TagBattery, $"{sp.BatteryLifePercent}%" + (sp.ACLineStatus == 1 ? " (charging)" : "")));
        if (Cfg.ShowUptime)
        {
            long sec = Environment.TickCount64 / 1000;
            int d = (int)(sec / 86400), h = (int)(sec / 3600 % 24), m = (int)(sec / 60 % 60);
            lines.Add(Tagged(Cfg.TagUptime, d > 0 ? $"{d}d {h}h" : $"{h}h {m}m"));
        }
    }
}
