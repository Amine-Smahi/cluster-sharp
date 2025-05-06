using System.Text.Json.Serialization;
using ClusterSharp.Api.Models.Stats;

namespace ClusterSharp.Api.Models;

public class ContainerStats
{
    public string Name { get; set; } = null!;
    public string? Cpu { get; set; }
    public Stat Memory { get; set; } = new();
    public Stat Disk { get; set; } = new();
    
    [JsonPropertyName("external_port")]
    public string ExternalPort { get; set; } = null!;
}