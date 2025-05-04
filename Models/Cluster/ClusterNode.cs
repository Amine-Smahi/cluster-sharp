namespace ClusterSharp.Api.Models.Cluster;

public class ClusterNode
{
    public string Hostname { get; set; }
    public string Role { get; set; }
    public MachineStats MachineStats { get; set; } = new();
    public List<ContainerInfo> Containers { get; set; } = [];
}