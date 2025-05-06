using System.Text.Json.Serialization;
using ClusterSharp.Api.Models.Stats;

namespace ClusterSharp.Api.Models.Cluster;

public class Node
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = null!;
    
    [JsonPropertyName("role")]
    public string Role { get; set; } = null!;
    
    public MachineStats MachineStats { get; set; } = new();
    public List<ContainerStats> Containers { get; set; } = [];
}