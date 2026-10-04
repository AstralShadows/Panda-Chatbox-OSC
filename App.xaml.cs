using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace PandaChatbox;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;   // the splash must not end the app when it closes
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Panda Chatbox couldn't start:\n\n" + ex.Message, "Panda Chatbox",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    async Task StartAsync()
    {
        // settings first, so we know the theme and whether the startup screen is wanted
        var (cfg, saved) = SettingsStore.Load();
        try
        {
            ThemeManager.Apply(cfg);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or FormatException or InvalidOperationException)
        {
            cfg.BackgroundMode = 0;
            ThemeManager.Apply(cfg);
            MessageBox.Show("The saved background couldn't be loaded, so the theme background will be used:\n\n" + ex.Message,
                "Panda Chatbox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        SplashWindow? splash = null;
        if (cfg.ShowSplash)
        {
            splash = new SplashWindow(new[]
            {
                "Dusting off your settings... Where did I put them?",
                "Picking out the perfect colors... Pandas love purple",
                "Building the main window... Bamboo, nails, UI!",
                "Warming up the OSC connection... Hello VRChat?",
                "Reading this PC's hardware... One moment",
                "Adding the finishing touches... Almost there!",
            });
            splash.Show();
        }

        int pause = splash == null ? 0 : 380;
        bool Stop()
        {
            if (splash is { Cancelled: true }) { Shutdown(); return true; }
            return false;
        }

        splash?.Next(); await Task.Delay(pause); if (Stop()) return;                       // 1 settings (already loaded)
        splash?.Next(); await Task.Delay(pause); if (Stop()) return;                       // 2 colors (already applied)
        splash?.Next(); await Task.Delay(splash == null ? 0 : 120); if (Stop()) return;    // 3 let the splash repaint, then build the UI
        var win = new MainWindow(cfg, saved);
        await Task.Delay(pause); if (Stop()) return;
        splash?.Next(); win.Begin(); await Task.Delay(pause); if (Stop()) return;          // 4 start engine + OSC
        splash?.Next(); await Task.Delay(pause); if (Stop()) return;                       // 5
        splash?.Next(); await Task.Delay(pause); if (Stop()) return;                       // 6
        splash?.Finish(); await Task.Delay(splash == null ? 0 : 300); if (Stop()) return;

        MainWindow = win;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        win.Show();
        splash?.Close();
    }
}
