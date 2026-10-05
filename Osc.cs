namespace PandaChatbox;

/// <summary>Sends VRChat OSC chatbox packets (/chatbox/input) over UDP.</summary>
public sealed class OscClient : IDisposable
{
    readonly SafeNativeHandle _handle = NativeInterop.CreateOsc();
    bool _configured;

    public string Error => NativeInterop.ReadOscError(_handle);

    public void Configure(string ip, int port)
    {
        _configured = NativeInterop.ConfigureOsc(_handle, ip, port);
    }

    public void Send(string text, bool immediate, bool sound)
    {
        if (!_configured)
            return;
        NativeInterop.SendOsc(_handle, text, immediate, sound);
    }

    public void Dispose() => _handle.Dispose();
}
