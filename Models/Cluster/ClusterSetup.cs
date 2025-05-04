using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models.Cluster;

public class ClusterSetup
{
    [JsonPropertyName("cluster")]
    public Admin Admin { get; set; }
    
    [JsonPropertyName("members")]
    public Member[] Members { get; set; } = [];
    
    public string ControllerHostname => Members.First(x => x.Role == Constants.Controller).Hostname;
}