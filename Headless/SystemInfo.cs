using Hardware.Info;

namespace Headless;

public class SystemInfo : FrooxEngine.StandaloneSystemInfo
{
    public SystemInfo()
    {
        var hwInfo = new HardwareInfo();
        hwInfo.RefreshOperatingSystem();
        hwInfo.RefreshCPUList(includePercentProcessorTime: false);
        hwInfo.RefreshMemoryStatus();
        base.OperatingSystem = hwInfo.OperatingSystem.Name;
        base.MemoryBytes = (long)hwInfo.MemoryStatus.TotalPhysical;
        var cpu = hwInfo.CpuList.FirstOrDefault();
        if (cpu != null)
        {
            base.CPU = cpu.Name;
            base.PhysicalCores = (int)cpu.NumberOfCores;
        }
    }
}