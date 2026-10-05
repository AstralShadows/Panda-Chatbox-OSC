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
    public double? UsedVramGb { get; private set; }
    public string VramUsageError { get; private set; } = "";
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
        ReadVramUsage();
    }

    void ReadVramUsage()
    {
        if (DxgiMemory.TryReadLocalUsage(GpuName, out ulong bytes, out string error))
        {
            UsedVramGb = bytes / BytesPerGb;
            VramUsageError = "";
        }
        else
        {
            UsedVramGb = null;
            VramUsageError = error;
        }
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

internal static class DxgiMemory
{
    const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    static readonly Guid Factory1Id = new("770aae78-f26f-4dba-a829-253c83d1b387");
    static readonly Guid Adapter3Id = new("645967A4-1392-4310-A798-8053CE3E93FD");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct AdapterDescription1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct VideoMemoryInfo
    {
        public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int EnumAdapters1Delegate(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetDescription1Delegate(IntPtr adapter, out AdapterDescription1 description);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int QueryVideoMemoryInfoDelegate(
        IntPtr adapter,
        uint nodeIndex,
        int segmentGroup,
        out VideoMemoryInfo memoryInfo);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

    public static bool TryReadLocalUsage(string adapterName, out ulong usageBytes, out string error)
    {
        usageBytes = 0;
        error = "";
        IntPtr factory = IntPtr.Zero;
        IntPtr selectedAdapter = IntPtr.Zero;

        try
        {
            Guid factoryId = Factory1Id;
            int result = CreateDXGIFactory1(ref factoryId, out factory);
            if (result < 0)
            {
                error = $"Windows GPU memory query failed (0x{result:X8}).";
                return false;
            }

            var enumerate = GetMethod<EnumAdapters1Delegate>(factory, 12);
            for (uint index = 0; ; index++)
            {
                result = enumerate(factory, index, out IntPtr adapter);
                if (result == DxgiErrorNotFound) break;
                if (result < 0)
                {
                    error = $"Windows GPU adapter enumeration failed (0x{result:X8}).";
                    return false;
                }

                var getDescription = GetMethod<GetDescription1Delegate>(adapter, 10);
                result = getDescription(adapter, out var description);
                if (result < 0)
                {
                    Marshal.Release(adapter);
                    error = $"Windows GPU adapter details failed (0x{result:X8}).";
                    return false;
                }

                if (string.Equals(description.Description?.Trim(), adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedAdapter = adapter;
                    break;
                }

                Marshal.Release(adapter);
            }

            if (selectedAdapter == IntPtr.Zero)
            {
                error = "Windows did not match the detected GPU to a graphics adapter.";
                return false;
            }

            Guid adapter3Id = Adapter3Id;
            result = Marshal.QueryInterface(selectedAdapter, ref adapter3Id, out IntPtr adapter3);
            if (result < 0)
            {
                error = $"The graphics driver does not expose VRAM usage (0x{result:X8}).";
                return false;
            }

            try
            {
                var query = GetMethod<QueryVideoMemoryInfoDelegate>(adapter3, 14);
                result = query(adapter3, 0, 0, out var memoryInfo);
                if (result < 0)
                {
                    error = $"The graphics driver could not report VRAM usage (0x{result:X8}).";
                    return false;
                }

                usageBytes = memoryInfo.CurrentUsage;
                return true;
            }
            finally
            {
                Marshal.Release(adapter3);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or
                                           MarshalDirectiveException or InvalidCastException)
        {
            error = $"VRAM usage is unavailable: {exception.Message}";
            return false;
        }
        finally
        {
            if (selectedAdapter != IntPtr.Zero) Marshal.Release(selectedAdapter);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
        }
    }

    static T GetMethod<T>(IntPtr instance, int index) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(instance);
        IntPtr method = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }
}
