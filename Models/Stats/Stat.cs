using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models;

public class DiskStat
{
    public string? Value { get; set; }
    
    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }
}