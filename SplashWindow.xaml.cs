using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PandaChatbox;

/// <summary>
/// Startup screen: progress bar plus three lines (last step with its time, the step running now, the next one).
/// App.xaml.cs calls Next() for each step and Finish() at the end. Cancel (or closing it early) quits the app.
/// </summary>
public partial class SplashWindow : Window
{
    readonly string[] _steps;
    readonly long[] _ms;
    readonly Stopwatch _sw = new();
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    int _cur = -1;
    bool _finished;

    public bool Cancelled { get; private set; }

    public SplashWindow(string[] steps)
    {
        InitializeComponent();
        _steps = steps;
        _ms = new long[steps.Length];

        _clock.Tick += (_, _) =>
        {
            if (_cur >= 0 && !_finished) CurTime.Text = Fmt(_sw.ElapsedMilliseconds);
        };
        _clock.Start();

        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>Finish the current step and start the next one.</summary>
    public void Next()
    {
        if (_cur >= 0) _ms[_cur] = _sw.ElapsedMilliseconds;
        _cur = Math.Min(_cur + 1, _steps.Length - 1);
        _sw.Restart();

        PrevText.Text = _cur > 0 ? _steps[_cur - 1] : "";
        PrevTime.Text = _cur > 0 ? Fmt(_ms[_cur - 1]) : "";
        CurText.Text = _steps[_cur];
        CurTime.Text = "";
        NextText.Text = _cur + 1 < _steps.Length ? _steps[_cur + 1] : "";
        SetProgress(_cur / (double)_steps.Length);
    }

    /// <summary>Mark everything done (bar to 100%).</summary>
    public void Finish()
    {
        if (_cur >= 0) _ms[_cur] = _sw.ElapsedMilliseconds;
        _finished = true;
        PrevText.Text = _cur >= 0 ? _steps[_cur] : "";
        PrevTime.Text = _cur >= 0 ? Fmt(_ms[_cur]) : "";
        CurText.Text = "All done! Opening Panda Chatbox...";
        CurTime.Text = "";
        NextText.Text = "";
        SetProgress(1);
    }

    void SetProgress(double p) =>
        BarScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(p, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    static string Fmt(long ms) => ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:0.0}s";

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Cancelled = true;
        Close();
    }

    void Drag_Down(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); } catch { /* ignore */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        if (!_finished) Cancelled = true;   // Alt+F4 before it finished counts as cancel
        base.OnClosed(e);
    }
}
