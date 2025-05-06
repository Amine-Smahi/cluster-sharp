using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models.Stats;

public class ContainerStats
{
    public string Name { get; set; } = null!;
    public double Cpu { get; set; }
    public double Memory { get; set; }
    public string Disk { get; set; } = null!;
    
    [JsonPropertyName("external_port")]
    public string ExternalPort { get; set; } = null!;
}