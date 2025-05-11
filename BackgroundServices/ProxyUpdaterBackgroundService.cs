using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.BackgroundServices
{
    public class ProxyUpdaterBackgroundService(
        ClusterOverviewService clusterOverviewService,
        ProxyRule proxyRule,
        ILogger<ProxyUpdaterBackgroundService> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    UpdateProxyRules();
                    logger.LogInformation("Proxy rules updated successfully at {time}", DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error updating proxy rules");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private void UpdateProxyRules()
        {
            var overview = clusterOverviewService.Overview;
            if (overview?.Containers == null || !overview.Containers.Any())
                return;
            
            proxyRule.Rules.Clear();
            
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
                proxyRule.Rules[domainKey] = sortedEndpoints;
            }
        }
    }
} 