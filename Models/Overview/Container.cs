using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models.Overview;

public class Container
{
    public string Name { get; set; } = null!;
    public int Replicas { get; set; }
    public string? ExternalPort { get; set; }
    public List<ContainerOnHostStats> ContainerOnHostStatsList { get; set; } = [];
    
    [JsonIgnore]
    public List<string> Hosts => ContainerOnHostStatsList.Select(x => x.Host).ToList();
}