using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models.Stats;

public class RequestStats
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("requestsPerSecond")]
    public double RequestsPerSecond { get; set; }
} 