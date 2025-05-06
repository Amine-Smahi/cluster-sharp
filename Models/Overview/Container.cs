namespace ClusterSharp.Api.Models.Overview;

public class Container
{
    public string Name { get; set; } = null!;
    public int Replicas { get; set; }
    public string? ExternalPort { get; set; }
    public List<string> Hosts { get; set; } = [];
}