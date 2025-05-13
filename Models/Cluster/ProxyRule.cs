using System.Collections.Concurrent;

namespace ClusterSharp.Api.Models.Cluster
{
    public interface IProxyRuleService
    {
        ConcurrentDictionary<string, List<string>> Rules { get; }
        bool TryGetEndpoints(string host, out List<string> endpoints);
        void SetRules(Dictionary<string, List<string>> rules);
        void ClearRules();
    }

    public class ProxyRuleService : IProxyRuleService
    {
        public ConcurrentDictionary<string, List<string>> Rules { get; } = new();

        public bool TryGetEndpoints(string host, out List<string> endpoints)
        {
            return Rules.TryGetValue(host, out endpoints);
        }

        public void SetRules(Dictionary<string, List<string>> rules)
        {
            ClearRules();
            foreach (var (key, value) in rules)
            {
                Rules[key] = value;
            }
        }

        public void ClearRules()
        {
            Rules.Clear();
        }
    }
} 