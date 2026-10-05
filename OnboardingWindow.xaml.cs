using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace PandaChatbox;

public partial class OnboardingWindow : Window
{
    readonly AppSettings _settings;
    readonly ObservableCollection<StatusItem> _messages;
    int _step;

    public OnboardingWindow(AppSettings settings, ObservableCollection<StatusItem> messages)
    {
        InitializeComponent();
        _settings = settings;
        _messages = messages;
        IpBox.Text = settings.Ip;
        PortBox.Text = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FirstStatusBox.Text = messages.FirstOrDefault()?.Text ?? "Hello, welcome to Panda Chatbox!";
        CpuCheck.IsChecked = settings.ShowCpu || settings.ShowRam || settings.ShowGpu || settings.ShowVram;
        MediaCheck.IsChecked = settings.ShowMedia;
        WeatherCheck.IsChecked = settings.ShowWeather;
        UpdateStep();
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            if (!int.TryParse(PortBox.Text, out int port) || port is < 1 or > 65535)
            {
                MessageBox.Show(this, "Enter a valid OSC port from 1 to 65535.", "Panda Chatbox setup",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PortBox.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(IpBox.Text))
            {
                MessageBox.Show(this, "Enter an OSC address. The default is 127.0.0.1.", "Panda Chatbox setup",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                IpBox.Focus();
                return;
            }
            _step++;
            UpdateStep();
            return;
        }

        if (_step == 1)
        {
            if (string.IsNullOrWhiteSpace(FirstStatusBox.Text))
            {
                MessageBox.Show(this, "Add a first status message, or use Skip for now.", "Panda Chatbox setup",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                FirstStatusBox.Focus();
                return;
            }
            _step++;
            UpdateStep();
            return;
        }

        ApplyChoices();
        _settings.OnboardingCompleted = true;
        DialogResult = true;
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step <= 0) return;
        _step--;
        UpdateStep();
    }

    void Skip_Click(object sender, RoutedEventArgs e)
    {
        _settings.OnboardingCompleted = true;
        DialogResult = false;
    }

    void TemplateHelper_Click(object sender, RoutedEventArgs e)
    {
        var helper = new TemplateBuilderWindow(FirstStatusBox.Text) { Owner = this };
        if (helper.ShowDialog() == true)
            FirstStatusBox.Text = helper.TemplateText;
    }

    void ApplyChoices()
    {
        _settings.Ip = IpBox.Text.Trim();
        _settings.Port = int.Parse(PortBox.Text, System.Globalization.CultureInfo.InvariantCulture);
        _settings.ShowCpu = CpuCheck.IsChecked == true;
        _settings.ShowRam = CpuCheck.IsChecked == true;
        _settings.ShowGpu = CpuCheck.IsChecked == true;
        _settings.ShowVram = CpuCheck.IsChecked == true;
        _settings.ShowMedia = MediaCheck.IsChecked == true;
        _settings.ShowWeather = WeatherCheck.IsChecked == true;
        if (_messages.Count == 0)
            _messages.Add(new StatusItem { Text = FirstStatusBox.Text.Trim(), Collection = "Default" });
        else
        {
            _messages[0].Text = FirstStatusBox.Text.Trim();
            _messages[0].Collection = "Default";
        }
    }

    void UpdateStep()
    {
        ConnectionPage.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusPage.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        IntegrationsPage.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepTitle.Text = _step switch
        {
            0 => "Connect Panda Chatbox to VRChat",
            1 => "Create your first status",
            _ => "Choose optional integrations"
        };
        StepSubtitle.Text = $"Step {_step + 1} of 3";
        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _step == 2 ? "Finish setup" : "Next";
    }
}
