using System.Text.Json.Serialization;

namespace ClusterSharp.Api.Models
{
    public class CommandsData
    {
        [JsonPropertyName("commands")]
        public string[] Commands { get; set; } = [];
    }
} 