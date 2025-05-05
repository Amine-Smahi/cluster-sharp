namespace ClusterSharp.Api.Models;

public class MachineStats
{
    public double CPU { get; set; }
    public MemStat Memory { get; set; } = new();
    public DiskStat Disk { get; set; } = new();
}