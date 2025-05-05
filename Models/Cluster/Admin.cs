using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models.Cluster;

public class Admin
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("password")]
    public string Password { get; set; } = null!;
}