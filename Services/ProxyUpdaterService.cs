using ClusterSharp.Api.Models.Cluster;

namespace ClusterSharp.Api.Services
{
    public class ProxyUpdaterService(
        ClusterOverviewService clusterOverviewService,
        IProxyRuleService proxyRuleService,
        ILogger<ProxyUpdaterService> logger)
    {
        private Dictionary<string, List<string>> _lastRuleSnapshot = new();

        public void UpdateProxyRulesIfNeeded()
        {
            try
            {
                var overview = clusterOverviewService.Overview;
                if (overview.Containers.Count == 0)
                    return;
                
                var newRules = new Dictionary<string, List<string>>();
                var hasChanges = false;
                
                foreach (var container in overview.Containers)
                {
                    if (container.ContainerOnHostStatsList.Count == 0)
                        continue;
                    
                    var sortedEndpoints = container.ContainerOnHostStatsList
                        .OrderBy(stats => stats.Cpu)
                        .ThenBy(stats => stats.Memory)
                        .Select(stats => $"{stats.Host}:{container.ExternalPort}")
                        .ToList();
                    
                    var domainKey = container.Name.ToLower();
                    newRules[domainKey] = sortedEndpoints;
                    
                    if (!_lastRuleSnapshot.TryGetValue(domainKey, out var lastEndpoints) || 
                        !lastEndpoints.SequenceEqual(sortedEndpoints))
                    {
                        hasChanges = true;
                    }
                }
                
                if (_lastRuleSnapshot.Keys.Count != newRules.Keys.Count) 
                    hasChanges = true;
                
                if (hasChanges)
                {
                    proxyRuleService.SetRules(newRules);
                    _lastRuleSnapshot = new Dictionary<string, List<string>>(newRules);
                    logger.LogInformation("Proxy rules updated");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating proxy rules");
            }
        }
    }
} 