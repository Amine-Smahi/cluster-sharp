using System.Collections.Concurrent;

namespace ClusterSharp.Api.Models.Cluster
{
    public class ProxyRule
    {
        public ConcurrentDictionary<string, List<string>> Rules { get; } = new();
    }
} 