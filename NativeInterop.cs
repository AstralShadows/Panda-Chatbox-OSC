using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PandaChatbox;

internal static class NativeInterop
{
    const string Library = "PandaChatbox.Native.dll";

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareSnapshot
    {
        public double CpuUsagePercent;
        public double UsedRamGb;
        public double TotalRamGb;
        public double TotalVramGb;
        public double UsedVramGb;
        public int HasUsedVram;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern IntPtr pch_hardware_create(StringBuilder error, int errorCapacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern void pch_hardware_destroy(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_hardware_read_info(SafeNativeHandle handle, int field, StringBuilder output, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_hardware_sample(SafeNativeHandle handle, out HardwareSnapshot snapshot, StringBuilder error, int errorCapacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern IntPtr pch_osc_create();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern void pch_osc_destroy(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_osc_configure(SafeNativeHandle handle, string ip, int port);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_osc_send(SafeNativeHandle handle, string text, int immediate, int sound);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    static extern int pch_osc_read_error(SafeNativeHandle handle, StringBuilder output, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern int pch_get_battery_status(out byte acLineStatus, out byte batteryFlag, out byte batteryLifePercent);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_get_foreground_window(
        StringBuilder appName,
        int appNameCapacity,
        StringBuilder executable,
        int executableCapacity,
        StringBuilder title,
        int titleCapacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_expand_template(
        string text,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] values,
        int count,
        StringBuilder? output,
        int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_apply_style(int style, string text, StringBuilder? output, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_pick_status(
        int mode,
        [In] int[] flags,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] collections,
        int count,
        string activeCollection,
        int currentIndex,
        int randomValue);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_compose_lines(
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] keys,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] texts,
        int count,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] order,
        int orderCount,
        StringBuilder? output,
        int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int pch_trim_chat_text(string text, int limit, StringBuilder? output, int capacity);

    internal static SafeNativeHandle CreateHardware()
    {
        var error = new StringBuilder(512);
        var pointer = pch_hardware_create(error, error.Capacity);
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException(error.Length > 0 ? error.ToString() : "Couldn't initialize the native hardware monitor.");
        return new SafeNativeHandle(pointer, pch_hardware_destroy);
    }

    internal static string ReadHardwareInfo(SafeNativeHandle handle, int field)
    {
        var buffer = new StringBuilder(512);
        var length = pch_hardware_read_info(handle, field, buffer, buffer.Capacity);
        if (length < 0)
            throw new InvalidOperationException("Couldn't read native hardware information.");
        return buffer.ToString(0, Math.Min(length, buffer.Length));
    }

    internal static HardwareSnapshot SampleHardware(SafeNativeHandle handle)
    {
        var error = new StringBuilder(512);
        if (pch_hardware_sample(handle, out var snapshot, error, error.Capacity) == 0)
            throw new InvalidOperationException(error.Length > 0 ? error.ToString() : "Couldn't sample hardware.");
        return snapshot;
    }

    internal static SafeNativeHandle CreateOsc()
    {
        var pointer = pch_osc_create();
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException("Couldn't initialize the native UDP networking library.");
        return new SafeNativeHandle(pointer, pch_osc_destroy);
    }

    internal static bool ConfigureOsc(SafeNativeHandle handle, string ip, int port) =>
        pch_osc_configure(handle, ip, port) != 0;

    internal static bool SendOsc(SafeNativeHandle handle, string text, bool immediate, bool sound) =>
        pch_osc_send(handle, text, immediate ? 1 : 0, sound ? 1 : 0) != 0;

    internal static string ReadOscError(SafeNativeHandle handle)
    {
        var buffer = new StringBuilder(256);
        var length = pch_osc_read_error(handle, buffer, buffer.Capacity);
        if (length < 0)
            throw new InvalidOperationException("Couldn't read the native OSC error state.");
        return buffer.ToString(0, Math.Min(length, buffer.Length));
    }

    internal static bool TryGetBatteryStatus(out Native.SYSTEM_POWER_STATUS status)
    {
        if (pch_get_battery_status(out var acLineStatus, out var batteryFlag, out var batteryLifePercent) == 0)
        {
            status = default;
            return false;
        }

        status = new Native.SYSTEM_POWER_STATUS
        {
            ACLineStatus = acLineStatus,
            BatteryFlag = batteryFlag,
            BatteryLifePercent = batteryLifePercent
        };
        return true;
    }

    internal static ForegroundWindowInfo? GetForegroundWindow()
    {
        var appName = new StringBuilder(512);
        var executable = new StringBuilder(512);
        var title = new StringBuilder(512);
        return pch_get_foreground_window(appName, appName.Capacity, executable, executable.Capacity, title, title.Capacity) == 0
            ? null
            : new ForegroundWindowInfo(appName.ToString(), executable.ToString(), title.ToString());
    }

    internal static string ExpandTemplate(string text, IReadOnlyDictionary<string, string> values)
    {
        var names = values.Keys.ToArray();
        var replacements = values.Values.ToArray();
        var length = pch_expand_template(text, names, replacements, names.Length, null, 0);
        if (length < 0)
            throw new InvalidOperationException("Couldn't expand the status template.");
        if (length == 0)
            return "";
        var output = new StringBuilder(length + 1);
        var written = pch_expand_template(text, names, replacements, names.Length, output, output.Capacity);
        if (written < 0 || written > length)
            throw new InvalidOperationException("The native status template expansion failed.");
        return output.ToString(0, written);
    }

    internal static string ApplyStatusStyle(int style, string text)
    {
        var length = pch_apply_style(style, text, null, 0);
        if (length < 0)
            throw new InvalidOperationException("Couldn't apply the status text style.");
        if (length == 0)
            return "";
        var output = new StringBuilder(length + 1);
        var written = pch_apply_style(style, text, output, output.Capacity);
        if (written < 0 || written > length)
            throw new InvalidOperationException("The native status text style conversion failed.");
        return output.ToString(0, written);
    }

    internal static int PickStatus(int mode, int[] flags, string[] collections, string? activeCollection, int currentIndex, int randomValue)
    {
        if (flags.Length == 0)
            return -1;
        var selected = pch_pick_status(mode, flags, collections, flags.Length, activeCollection ?? "", currentIndex, randomValue);
        if (selected < -1 || selected >= flags.Length)
            throw new InvalidOperationException("The native status selector returned an invalid message index.");
        return selected;
    }

    internal static string ComposeLines((string Key, string Text)[] entries, string[] order)
    {
        var keys = entries.Select(entry => entry.Key).ToArray();
        var texts = entries.Select(entry => entry.Text).ToArray();
        var length = pch_compose_lines(keys, texts, entries.Length, order, order.Length, null, 0);
        if (length < 0)
            throw new InvalidOperationException("Couldn't compose the chatbox text.");
        if (length == 0)
            return "";
        var output = new StringBuilder(length + 1);
        var written = pch_compose_lines(keys, texts, entries.Length, order, order.Length, output, output.Capacity);
        if (written < 0 || written > length)
            throw new InvalidOperationException("The native chatbox text composition failed.");
        return output.ToString(0, written);
    }

    internal static string TrimChatText(string text, int limit)
    {
        var length = pch_trim_chat_text(text, limit, null, 0);
        if (length < 0)
            throw new InvalidOperationException("Couldn't trim the chatbox text.");
        if (length == 0)
            return "";
        var output = new StringBuilder(length + 1);
        var written = pch_trim_chat_text(text, limit, output, output.Capacity);
        if (written < 0 || written > length)
            throw new InvalidOperationException("The native chatbox text trimming failed.");
        return output.ToString(0, written);
    }
}

internal sealed class SafeNativeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    readonly Action<IntPtr> _release;

    internal SafeNativeHandle(IntPtr handle, Action<IntPtr> release) : base(true)
    {
        _release = release;
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        _release(handle);
        return true;
    }
}
