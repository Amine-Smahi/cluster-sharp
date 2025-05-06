using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models.Stats;

public class Stat
{
    public string? Value { get; set; }
    
    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }
}