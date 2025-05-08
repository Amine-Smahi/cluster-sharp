using ClusterSharp.Api.Models.Overview;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using Yarp.ReverseProxy.Configuration;

namespace ClusterSharp.Api.Services;

public class ClusterYarpService
{
    private readonly ClusterOverviewService _overviewService;
    private readonly ILogger<ClusterYarpService> _logger;
    private readonly InMemoryConfigProvider _proxyConfigProvider;
    
    // Cache to avoid unnecessary recreation of destination configs
    private readonly ConcurrentDictionary<string, DestinationConfig> _destinationCache = new();

    public ClusterYarpService(
        ClusterOverviewService overviewService,
        InMemoryConfigProvider proxyConfigProvider,
        ILogger<ClusterYarpService> logger)
    {
        _overviewService = overviewService;
        _proxyConfigProvider = proxyConfigProvider;
        _logger = logger;
        
        // Subscribe to overview updates to refresh the proxy configuration
        _overviewService.OverviewUpdated += (_, _) => UpdateProxyConfig();
    }

    public void UpdateProxyConfig()
    {
        try
        {
            var overview = _overviewService.Overview;
            if (overview?.Containers == null || !overview.Containers.Any())
            {
                _logger.LogWarning("No containers found in overview");
                return;
            }

            var routes = new List<RouteConfig>();
            var clusters = new List<ClusterConfig>();

            foreach (var container in overview.Containers)
            {
                if (string.IsNullOrEmpty(container.ExternalPort) || container.Hosts.Count == 0)
                    continue;

                var clusterId = $"cluster-{container.Name}";
                
                // Create destinations for each host running this container
                var destinations = new Dictionary<string, DestinationConfig>(container.ContainerOnHostStatsList.Count);
                foreach (var hostStats in container.ContainerOnHostStatsList)
                {
                    var destId = $"{container.Name}-{hostStats.Host}";
                    var destKey = $"{destId}-{container.ExternalPort}";
                    
                    // Try to get from cache first
                    if (!_destinationCache.TryGetValue(destKey, out var destinationConfig))
                    {
                        destinationConfig = new DestinationConfig
                        {
                            Address = $"http://{hostStats.Host}:{container.ExternalPort}"
                        };
                        
                        _destinationCache[destKey] = destinationConfig;
                    }
                    
                    destinations.Add(destId, destinationConfig);
                }

                // Create cluster config with health checks
                clusters.Add(new ClusterConfig
                {
                    ClusterId = clusterId,
                    LoadBalancingPolicy = "PowerOfTwoChoices", // Efficient load balancing suitable for high traffic
                    HealthCheck = new HealthCheckConfig
                    {
                        Passive = new PassiveHealthCheckConfig
                        {
                            Enabled = true,
                            Policy = "TransportFailureRate"
                        }
                    },
                    HttpClient = new HttpClientConfig
                    {
                        MaxConnectionsPerServer = 1000, // High connection limit for high traffic
                        EnableMultipleHttp2Connections = true,
                        DangerousAcceptAnyServerCertificate = false
                    },
                    Destinations = destinations
                });

                // Create host-based route config (route based on the domain rather than path)
                routes.Add(new RouteConfig
                {
                    RouteId = $"route-{container.Name}",
                    ClusterId = clusterId,
                    Match = new RouteMatch
                    {
                        // Match requests by host header (domain name) instead of path
                        Hosts = new[] { container.Name },
                        Path = "/{**catch-all}"
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "ResponseBuffering", "false" },  // Stream responses for lower latency
                        { "Timeout", "00:01:00" }          // 1 minute timeout
                    }
                });
                
                // Also add a path-based route for compatibility
                routes.Add(new RouteConfig
                {
                    RouteId = $"path-route-{container.Name}",
                    ClusterId = clusterId,
                    Match = new RouteMatch
                    {
                        Path = $"/{container.Name}/{{**catch-all}}"
                    },
                    Transforms = new List<Dictionary<string, string>>
                    {
                        new() { { "PathRemovePrefix", $"/{container.Name}" } }
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "ResponseBuffering", "false" },
                        { "Timeout", "00:01:00" }
                    }
                });
            }

            // Update the configuration
            _proxyConfigProvider.Update(routes, clusters);
            
            _logger.LogInformation("Updated YARP proxy configuration with {RouteCount} routes and {ClusterCount} clusters", 
                routes.Count, clusters.Count);
            
            // Clean cache entries that are no longer used
            CleanDestinationCache(overview.Containers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating proxy configuration");
        }
    }
    
    private void CleanDestinationCache(List<Container> containers)
    {
        // Build a set of currently active destination keys
        var activeKeys = new HashSet<string>();
        foreach (var container in containers)
        {
            if (string.IsNullOrEmpty(container.ExternalPort))
                continue;
                
            foreach (var hostStats in container.ContainerOnHostStatsList)
            {
                var destId = $"{container.Name}-{hostStats.Host}";
                var destKey = $"{destId}-{container.ExternalPort}";
                activeKeys.Add(destKey);
            }
        }
        
        // Remove cached entries that are no longer active
        foreach (var key in _destinationCache.Keys)
        {
            if (!activeKeys.Contains(key))
            {
                _destinationCache.TryRemove(key, out _);
            }
        }
    }
} 