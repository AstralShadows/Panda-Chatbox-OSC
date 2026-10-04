using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PandaChatbox;

internal sealed class PcHardwareMonitor
{
    const double BytesPerGb = 1024.0 * 1024 * 1024;
    const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    long _lastIdle, _lastKernel, _lastUser;

    public string CpuName { get; }
    public string GpuName { get; }
    public double TotalRamGb { get; private set; }
    public double TotalVramGb { get; }
    public double CpuUsagePercent { get; private set; }
    public double UsedRamGb { get; private set; }

    public PcHardwareMonitor()
    {
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        CpuName = cpu?.GetValue("ProcessorNameString") as string
            ?? throw new InvalidOperationException("Windows did not report a CPU model.");

        (GpuName, TotalVramGb) = ReadGraphicsAdapter();
        ReadMemory();
    }

    public void Sample()
    {
        SampleCpu();
        ReadMemory();
    }

    void SampleCpu()
    {
        if (!NativeHardware.GetSystemTimes(out var idle, out var kernel, out var user))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't read CPU usage.");

        long idleNow = FileTimeValue(idle);
        long kernelNow = FileTimeValue(kernel);
        long userNow = FileTimeValue(user);
        if (_lastKernel != 0 || _lastUser != 0)
        {
            long total = (kernelNow - _lastKernel) + (userNow - _lastUser);
            long busy = total - (idleNow - _lastIdle);
            CpuUsagePercent = total <= 0 ? 0 : Math.Clamp(busy * 100.0 / total, 0, 100);
        }

        _lastIdle = idleNow;
        _lastKernel = kernelNow;
        _lastUser = userNow;
    }

    void ReadMemory()
    {
        var status = new NativeHardware.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeHardware.MemoryStatusEx>() };
        if (!NativeHardware.GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't read system memory usage.");

        TotalRamGb = status.TotalPhysical / BytesPerGb;
        UsedRamGb = (status.TotalPhysical - status.AvailablePhysical) / BytesPerGb;
    }

    static (string name, double memoryGb) ReadGraphicsAdapter()
    {
        using var displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKey)
            ?? throw new InvalidOperationException("Windows did not report any display adapters.");

        string bestName = "";
        ulong bestMemory = 0;
        foreach (string adapterKey in displayClass.GetSubKeyNames())
        {
            if (adapterKey.Length != 4 || !uint.TryParse(adapterKey, out _)) continue;
            using var adapter = displayClass.OpenSubKey(adapterKey);
            if (adapter == null) continue;

            string? name = adapter.GetValue("DriverDesc") as string
                ?? adapter.GetValue("Device Description") as string;
            if (string.IsNullOrWhiteSpace(name)) continue;

            ulong memory = ReadDedicatedMemory(adapter);
            if (bestName.Length > 0 && memory <= bestMemory) continue;
            bestName = name.Trim();
            bestMemory = memory;
        }

        if (bestName.Length == 0)
            throw new InvalidOperationException("Windows did not report a display adapter model.");

        return (bestName, bestMemory / BytesPerGb);
    }

    static ulong ReadDedicatedMemory(RegistryKey adapter)
    {
        object? value = adapter.GetValue("HardwareInformation.qwMemorySize")
            ?? adapter.GetValue("HardwareInformation.MemorySize");
        return value switch
        {
            byte[] bytes when bytes.Length >= sizeof(ulong) => BitConverter.ToUInt64(bytes, 0),
            byte[] bytes when bytes.Length >= sizeof(uint) => BitConverter.ToUInt32(bytes, 0),
            long bytes when bytes > 0 => (ulong)bytes,
            int bytes when bytes > 0 => (uint)bytes,
            ulong bytes => bytes,
            uint bytes => bytes,
            _ => 0
        };
    }

    static long FileTimeValue(System.Runtime.InteropServices.ComTypes.FILETIME value) =>
        ((long)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;
}

internal static class NativeHardware
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }
}
