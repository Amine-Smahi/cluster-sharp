namespace ClusterSharp.Api.Models;

public class MachineStats
{
    public double Cpu { get; set; }
    public MemStat Memory { get; set; } = new();
    public DiskStat Disk { get; set; } = new();
}