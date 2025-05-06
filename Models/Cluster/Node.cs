using ClusterSharp.Api.Models.Stats;

namespace ClusterSharp.Api.Models.Cluster;

public class ClusterNode
{
    public string Hostname { get; set; } = null!;
    public string Role { get; set; } = null!;
    public MachineStats MachineStats { get; set; } = new();
    public List<ContainerStats> Containers { get; set; } = [];
}