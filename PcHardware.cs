namespace PandaChatbox;

internal sealed class PcHardwareMonitor
{
    readonly SafeNativeHandle _handle;

    public string CpuName { get; }
    public string GpuName { get; }
    public double TotalRamGb { get; private set; }
    public double TotalVramGb { get; private set; }
    public double? UsedVramGb { get; private set; }
    public string VramUsageError { get; private set; } = "";
    public double CpuUsagePercent { get; private set; }
    public double UsedRamGb { get; private set; }

    public PcHardwareMonitor()
    {
        _handle = NativeInterop.CreateHardware();
        CpuName = NativeInterop.ReadHardwareInfo(_handle, 0);
        GpuName = NativeInterop.ReadHardwareInfo(_handle, 1);
        Sample();
    }

    public void Sample()
    {
        var snapshot = NativeInterop.SampleHardware(_handle);
        CpuUsagePercent = snapshot.CpuUsagePercent;
        UsedRamGb = snapshot.UsedRamGb;
        TotalRamGb = snapshot.TotalRamGb;
        TotalVramGb = snapshot.TotalVramGb;
        UsedVramGb = snapshot.HasUsedVram != 0 ? snapshot.UsedVramGb : null;
        VramUsageError = NativeInterop.ReadHardwareInfo(_handle, 2);
    }
}
