namespace ClusterSharp.Api.Models;

public class ClusterOverview
{
    public List<Container> Containers { get; set; } = [];
    public List<Machine> Machines { get; set; } = [];
}

public class Machine
{
    public string? Hostname { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
}

public class Container
{
    public string Name { get; set; } = null!;
    public int Replicas { get; set; }
    public string? ExternalPort { get; set; }
    public List<string> Hosts { get; set; } = [];
}