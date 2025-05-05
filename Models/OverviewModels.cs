namespace ClusterSharp.Api.Models;

public class ClusterOverview
{
    public string Name { get; set; }
    public int Replicas { get; set; }
    public List<HostInfo> Hosts { get; set; }
}

public class HostInfo
{
    public string Hostname { get; set; }
    public double CPU { get; set; }
    public string Memory { get; set; }
    public double MemoryPercent { get; set; }
    public string ExternalPort { get; set; }
}