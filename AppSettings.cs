using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PandaChatbox;

/// <summary>
/// Everything the user can change. Bound straight to the UI (the window's DataContext).
/// Setters throw on invalid input so the text box shows a red outline.
/// </summary>
public class AppSettings
{
    // While loading a saved file we silently ignore bad values instead of throwing.
    public static bool Loading;

    static bool Bad(bool invalid, string msg)
    {
        if (!invalid) return false;
        if (Loading) return true;
        throw new ArgumentException(msg);
    }
    static bool BadNum(double v) => double.IsNaN(v) || double.IsInfinity(v) || v < 0;

    // ---- general ----
    public bool Send { get; set; } = true;
    public bool Immediate { get; set; } = true;
    public bool Sound { get; set; } = true;
    public bool Topmost { get; set; }
    public bool CustomWindowControls { get; set; } = true;
    public bool TrimToLimit { get; set; } = true;     // cut text at 144 characters before sending
    public bool ShowSplash { get; set; } = true;      // startup screen

    int _themeIndex;
    public int ThemeIndex { get => _themeIndex; set => _themeIndex = Math.Clamp(value, 0, ThemeManager.Names.Length - 1); }
    double _surfaceTransparency = 0.22;
    public double SurfaceTransparency
    {
        get => _surfaceTransparency;
        set { if (Bad(BadNum(value) || value > 0.75, "0-0.75")) return; _surfaceTransparency = value; }
    }
    string _textColor = "#FFFFFF";
    public string TextColor
    {
        get => _textColor;
        set { if (Bad(!ThemeManager.IsValidTextColor(value), "Use a hex color such as #FFFFFF")) return; _textColor = value; }
    }

    int _backgroundMode;
    public int BackgroundMode { get => _backgroundMode; set => _backgroundMode = Math.Clamp(value, 0, 2); }
    double _backgroundHue = 265, _backgroundSaturation = 0.65, _backgroundBrightness = 0.22;
    public double BackgroundHue { get => _backgroundHue; set { if (Bad(BadNum(value) || value > 360, "0-360")) return; _backgroundHue = value; } }
    public double BackgroundSaturation { get => _backgroundSaturation; set { if (Bad(BadNum(value) || value > 1, "0-1")) return; _backgroundSaturation = value; } }
    public double BackgroundBrightness { get => _backgroundBrightness; set { if (Bad(BadNum(value) || value > 1, "0-1")) return; _backgroundBrightness = value; } }
    public string BackgroundImagePath { get; set; } = "";

    // ---- status options ----
    int _statusMode;   // 0 = in order, 1 = random, 2 = favorites only
    public int StatusMode { get => _statusMode; set => _statusMode = Math.Clamp(value, 0, 2); }
    public string StatusPrefix { get; set; } = "";
    public string StatusSuffix { get; set; } = "";

    // ---- timing ----
    double _messageDelay = 3.0, _chatDuration = 4.0, _minSend, _hardwareUpdateInterval = 1.0;
    public double MessageDelay { get => _messageDelay; set { if (Bad(BadNum(value) || value < 0.1, "Too small")) return; _messageDelay = value; } }
    public double ChatDuration { get => _chatDuration; set { if (Bad(BadNum(value) || value < 0.1, "Too small")) return; _chatDuration = value; } }
    /// <summary>Minimum seconds between automatic sends (0 = no limit). Handy because VRChat throttles fast updates.</summary>
    public double MinSendInterval { get => _minSend; set { if (Bad(BadNum(value) || value > 10, "0-10")) return; _minSend = value; } }
    public double HardwareUpdateInterval
    {
        get => _hardwareUpdateInterval;
        set { if (Bad(BadNum(value) || value < 0.5 || value > 10, "0.5-10")) return; _hardwareUpdateInterval = value; }
    }

    // ---- connection ----
    string _ip = "127.0.0.1";
    int _port = 9000;
    public string Ip { get => _ip; set { if (Bad(string.IsNullOrWhiteSpace(value), "Required")) return; _ip = value.Trim(); } }
    public int Port { get => _port; set { if (Bad(value < 1 || value > 65535, "1-65535")) return; _port = value; } }

    public bool ShowStatus { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowVram { get; set; } = true;

    // extra stat tweaks
    public bool ShowCpuName { get; set; } = true;
    public bool ShowRamPercent { get; set; } = true;
    public string StatSeparator { get; set; } = " | ";

    // ---- integrations (off by default so the chatbox stays short) ----
    public bool ShowTime { get; set; }
    public bool ShowDate { get; set; }
    public bool H24 { get; set; }
    public bool ShowSeconds { get; set; }
    int _dateStyle;
    public int DateStyle { get => _dateStyle; set => _dateStyle = Math.Clamp(value, 0, 4); }

    public bool ShowWeather { get; set; }
    public bool Fahrenheit { get; set; } = !RegionInfo.CurrentRegion.IsMetric;
    public string City { get; set; } = "";
    int _weatherMinutes = 10;
    public int WeatherMinutes { get => _weatherMinutes; set { if (Bad(value < 1 || value > 120, "1-120")) return; _weatherMinutes = value; } }

    public bool ShowMedia { get; set; }
    public bool MediaSpotify { get; set; } = true;
    public bool MediaYoutube { get; set; } = true;
    public bool MediaOther { get; set; } = true;
    public bool ShowMediaApp { get; set; } = true;    // "(Spotify)" after the song
    int _mediaMax = 50;
    public int MediaMax { get => _mediaMax; set { if (Bad(value < 10 || value > 100, "10-100")) return; _mediaMax = value; } }

    public bool ShowApp { get; set; }
    public bool ShowBattery { get; set; }
    public bool ShowUptime { get; set; }

    // ---- editable line labels (blank = show the line with no label) ----
    public string TagCpu { get; set; } = "[CPU]";
    public string TagRam { get; set; } = "[RAM]";
    public string TagGpu { get; set; } = "[GPU]";
    public string TagVram { get; set; } = "[VRAM]";
    public string TagTime { get; set; } = "[TIME]";
    public string TagDate { get; set; } = "[DATE]";
    public string TagWeather { get; set; } = "[WEATHER]";
    public string TagMusic { get; set; } = "[MUSIC]";
    public string TagApp { get; set; } = "[APP]";
    public string TagBattery { get; set; } = "[BATTERY]";
    public string TagUptime { get; set; } = "[UPTIME]";
}

public class SaveFile
{
    public AppSettings Settings { get; set; } = new();
    public List<StatusItem>? Items { get; set; }

    // old v2.0 format (plain strings). Still read so nobody loses their list, never written again.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Messages { get; set; }
}

/// <summary>Saves/loads settings + status messages to %APPDATA%\PandaChatbox\settings.json</summary>
public static class SettingsStore
{
    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PandaChatbox");
    public static string FilePath => Path.Combine(Dir, "settings.json");
    static string? _lastSaved;
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static (AppSettings settings, List<StatusItem>? items) Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return (new AppSettings(), null);
            AppSettings.Loading = true;
            var sf = JsonSerializer.Deserialize<SaveFile>(File.ReadAllText(FilePath));
            var items = sf?.Items ?? sf?.Messages?.Select(m => new StatusItem { Text = m }).ToList();
            return (sf?.Settings ?? new AppSettings(), items);
        }
        catch
        {
            return (new AppSettings(), null);   // corrupt file: start fresh
        }
        finally
        {
            AppSettings.Loading = false;
        }
    }

    public static void Save(AppSettings settings, IEnumerable<StatusItem> items)
    {
        try
        {
            string json = JsonSerializer.Serialize(new SaveFile { Settings = settings, Items = items.ToList() }, Opts);
            if (json == _lastSaved) return;
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, true);
            _lastSaved = json;
        }
        catch
        {
            // ignore: saving is best-effort
        }
    }
}
