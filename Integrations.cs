using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Media.Control;

namespace PandaChatbox;

internal static class Native
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}

internal record WinInfo(string Exe, string Title, bool Visible);
internal record ForegroundWindowInfo(string AppName, string Executable, string Title);

internal static class Desktop
{
    /// <summary>All top-level windows that have a title, with their process exe name (lowercase, e.g. "spotify.exe").</summary>
    public static List<WinInfo> List()
    {
        var list = new List<WinInfo>();
        var cache = new Dictionary<uint, string>();
        Native.EnumWindows((h, _) =>
        {
            var sb = new StringBuilder(512);
            if (Native.GetWindowText(h, sb, 512) <= 0) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (!cache.TryGetValue(pid, out string? exe))
            {
                try { exe = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant() + ".exe"; }
                catch { exe = ""; }
                cache[pid] = exe;
            }
            if (exe.Length > 0) list.Add(new WinInfo(exe, sb.ToString(), Native.IsWindowVisible(h)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string? ForegroundApp()
        => ForegroundWindow()?.AppName;

    public static ForegroundWindowInfo? ForegroundWindow()
    {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid == Environment.ProcessId) return null;   // ignore ourselves
        try
        {
            string processName = Process.GetProcessById((int)pid).ProcessName;
            if (processName.Length == 0) return null;
            var title = new StringBuilder(512);
            Native.GetWindowText(h, title, title.Capacity);
            return new ForegroundWindowInfo(
                char.ToUpperInvariant(processName[0]) + processName[1..],
                processName + ".exe",
                title.ToString());
        }
        catch { return null; }
    }
}

/// <summary>Weather from wttr.in (no API key needed).</summary>
public sealed class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    public string Text { get; private set; } = "";
    public string Status { get; private set; } = "Waiting...";

    public async Task<bool> FetchAsync(string city, bool fahrenheit)
    {
        Status = "Fetching...";
        try
        {
            string url = $"https://wttr.in/{Uri.EscapeDataString(city)}?format=%25C+%25t&{(fahrenheit ? "u" : "m")}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("PandaChatbox/2.0 (curl)");
            using var res = await Http.SendAsync(req);
            string txt = (await res.Content.ReadAsStringAsync()).Trim();
            bool good = res.IsSuccessStatusCode && txt.Length > 0 && txt.Length < 60 &&
                        !txt.Contains('<') && !txt.Contains("Unknown");
            if (good)
            {
                Text = txt;
                Status = txt;
                return true;
            }
            Status = "Couldn't find that city";
            return false;
        }
        catch
        {
            Status = "No connection (retrying)";
            return false;
        }
    }
}

/// <summary>
/// Now playing. Uses the Windows media session API first (real play/pause state, works with Spotify, YouTube in
/// Chrome/Edge, Apple Music, TIDAL...). If that finds nothing it falls back to reading player window titles.
/// </summary>
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;

    static readonly (string key, string name)[] BrowserNames =
    {
        ("msedge", "Edge"), ("edge", "Edge"), ("chrome", "Chrome"), ("firefox", "Firefox"),
        ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi"), ("arc", "Arc"),
    };
    static readonly string[] BrowserExes =
        { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe" };
    static readonly string[] YtSkip = { "Home", "Subscriptions", "Shorts", "Library", "History", "Explore", "Trending" };

    // exe, app name, window-title suffix to strip
    static readonly (string exe, string app, string suffix)[] Players =
    {
        ("vlc.exe", "VLC", " - VLC media player"),
        ("foobar2000.exe", "foobar2000", " [foobar2000]"),
        ("musicbee.exe", "MusicBee", " - MusicBee"),
        ("aimp.exe", "AIMP", ""),
        ("tidal.exe", "TIDAL", ""),
        ("deezer.exe", "Deezer", ""),
        ("amazonmusic.exe", "Amazon Music", ""),
        ("wmplayer.exe", "Windows Media Player", ""),
        ("mpc-hc64.exe", "MPC-HC", ""),
    };

    public async Task<(string app, string text)> ScanAsync(bool spotify, bool youtube, bool other)
    {
        var wins = Desktop.List();

        // 1) Windows media session
        try
        {
            _mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            foreach (var s in _mgr.GetSessions())
            {
                if (s.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    continue;
                var p = await s.TryGetMediaPropertiesAsync();
                string title = p.Title ?? "";
                string artist = p.Artist ?? "";
                if (title.Length == 0) continue;
                string app = FriendlyApp(s.SourceAppUserModelId, title, wins);
                if (!Allowed(app, spotify, youtube, other)) continue;
                return (app, artist.Length > 0 ? $"{artist} - {title}" : title);
            }
        }
        catch
        {
            _mgr = null;   // try to reconnect next scan
        }

        // 2) fallback: window titles
        return TitleScan(wins, spotify, youtube, other);
    }

    static bool Allowed(string app, bool spotify, bool youtube, bool other) =>
        app == "Spotify" ? spotify :
        app.StartsWith("YouTube") ? youtube :
        other;

    static string FriendlyApp(string? aumid, string mediaTitle, List<WinInfo> wins)
    {
        string id = (aumid ?? "").ToLowerInvariant();
        if (id.Contains("spotify")) return "Spotify";

        foreach (var (key, name) in BrowserNames)
        {
            if (!id.Contains(key)) continue;
            // is it YouTube? look for a browser window whose title shows it
            string probe = mediaTitle.Length > 20 ? mediaTitle[..20] : mediaTitle;
            foreach (var w in wins)
            {
                if (!BrowserExes.Contains(w.Exe) || !w.Title.Contains(probe)) continue;
                if (w.Title.Contains(" - YouTube Music")) return "YouTube Music";
                if (w.Title.Contains(" - YouTube")) return "YouTube";
            }
            return name;
        }

        if (id.Contains("applemusic") || id.Contains("itunes")) return "Apple Music";
        if (id.Contains("zunemusic") || id.Contains("mediaplayer")) return "Media Player";
        if (id.Contains("tidal")) return "TIDAL";
        if (id.Contains("deezer")) return "Deezer";
        if (id.Contains("amazonmusic")) return "Amazon Music";
        if (id.Contains("vlc")) return "VLC";
        if (id.Contains("foobar")) return "foobar2000";
        if (id.Contains("musicbee")) return "MusicBee";
        if (id.Contains("aimp")) return "AIMP";

        string clean = (aumid ?? "Media").Split('!')[0];
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) clean = clean[..^4];
        return clean.Length == 0 ? "Media" : clean;
    }

    static (string app, string text) TitleScan(List<WinInfo> wins, bool spotify, bool youtube, bool other)
    {
        int bestPrio = int.MaxValue;
        string bestApp = "", bestText = "";
        void Consider(int prio, string app, string text)
        {
            if (prio < bestPrio) { bestPrio = prio; bestApp = app; bestText = text; }
        }

        foreach (var w in wins)
        {
            if (w.Exe == "spotify.exe")   // desktop app: title is "Artist - Song" while playing
            {
                if (!spotify || !w.Title.Contains(" - ") || w.Title.StartsWith("GDI+")) continue;
                Consider(0, "Spotify", w.Title);
                continue;
            }
            if (BrowserExes.Contains(w.Exe))
            {
                if (!youtube || !w.Visible) continue;
                string app = "YouTube";
                int p = w.Title.LastIndexOf(" - YouTube Music", StringComparison.Ordinal);
                if (p >= 0) app = "YouTube Music";
                else p = w.Title.LastIndexOf(" - YouTube", StringComparison.Ordinal);
                if (p <= 0) continue;
                string t = w.Title[..p];
                if (t.Length > 3 && t[0] == '(')   // notification count like "(2) "
                {
                    int q = t.IndexOf(") ", StringComparison.Ordinal);
                    if (q >= 0 && q < 6) t = t[(q + 2)..];
                }
                if (YtSkip.Contains(t)) continue;
                Consider(1, app, t);
                continue;
            }
            if (!other || !w.Visible) continue;
            foreach (var (exe, app, suffix) in Players)
            {
                if (w.Exe != exe) continue;
                string t = w.Title;
                if (suffix.Length > 0 && t.EndsWith(suffix, StringComparison.Ordinal)) t = t[..^suffix.Length];
                if (t.Length == 0 || t.Equals(app, StringComparison.OrdinalIgnoreCase)) break;   // idle
                Consider(2, app, t);
                break;
            }
        }
        return (bestApp, bestText);
    }
}
