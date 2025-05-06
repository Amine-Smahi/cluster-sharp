namespace ClusterSharp.Api.Models.Overview;

public class Machine
{
    public string? Hostname { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
}