namespace ClusterSharp.Api.Models;

public class MachineStats
{
    public string CPU { get; set; }
    public MemStat Memory { get; set; } = new();
    public DiskStat Disk { get; set; } = new();
}