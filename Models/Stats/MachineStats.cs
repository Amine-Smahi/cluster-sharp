namespace ClusterSharp.Api.Models.Stats;

public class MachineStats
{
    public double Cpu { get; set; }
    public Stat Memory { get; set; } = new();
    public Stat Disk { get; set; } = new();
}