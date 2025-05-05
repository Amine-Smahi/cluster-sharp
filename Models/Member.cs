using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models;

public class Member
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}