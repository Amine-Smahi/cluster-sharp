namespace ClusterSharp.Api.Models.Overview;

public class ClusterOverview
{
    public List<Container> Containers { get; set; } = [];
    public List<Machine> Machines { get; set; } = [];
}