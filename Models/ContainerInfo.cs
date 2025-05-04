using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models;

public class ContainerInfo
{
    public string Name { get; set; }
    public string CPU { get; set; }
    public MemStat Memory { get; set; } = new();
    public DiskStat Disk { get; set; } = new();
    
    [JsonPropertyName("external_port")]
    public string ExternalPort { get; set; }
}