using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PandaChatbox;

/// <summary>Sends VRChat OSC chatbox packets (/chatbox/input) over UDP.</summary>
public sealed class OscClient : IDisposable
{
    UdpClient? _udp;
    IPEndPoint? _ep;
    public string Error { get; private set; } = "";

    public void Configure(string ip, int port)
    {
        _udp?.Dispose();
        _udp = null;
        _ep = null;
        if (!IPAddress.TryParse(ip, out var addr))
        {
            Error = "Bad IP address";
            return;
        }
        try
        {
            _udp = new UdpClient(addr.AddressFamily);
            _ep = new IPEndPoint(addr, port);
            Error = "";
        }
        catch
        {
            Error = "Couldn't open socket";
        }
    }

    public void Send(string text, bool immediate, bool sound)
    {
        if (_udp == null || _ep == null) return;
        try
        {
            byte[] pkt = Build(text, immediate, sound);
            _udp.Send(pkt, pkt.Length, _ep);
            Error = "";
        }
        catch
        {
            Error = "Send failed";
        }
    }

    // OSC message: address, type tags (",s" + two bools T/F), then the string. Each part is null-padded to 4 bytes.
    static byte[] Build(string text, bool immediate, bool sound)
    {
        var b = new List<byte>();
        AddPadded(b, "/chatbox/input");
        AddPadded(b, ",s" + (immediate ? "T" : "F") + (sound ? "T" : "F"));
        AddPadded(b, text);
        return b.ToArray();
    }

    static void AddPadded(List<byte> b, string s)
    {
        b.AddRange(Encoding.UTF8.GetBytes(s));
        b.Add(0);
        while (b.Count % 4 != 0) b.Add(0);
    }

    public void Dispose() => _udp?.Dispose();
}
