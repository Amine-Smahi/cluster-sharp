using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models.Cluster;

public class ClusterSetup
{
    [JsonPropertyName("admin")]
    public Admin Admin { get; set; } = null!;

    [JsonPropertyName("members")]
    public Node[] Nodes { get; set; } = [];
    
    public string ControllerHostname => Nodes.First(x => x.Role == Constants.Controller).Hostname;
}