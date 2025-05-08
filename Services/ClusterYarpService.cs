using ClusterSharp.Api.Models.Overview;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Security.Authentication;
using Yarp.ReverseProxy.Configuration;

namespace ClusterSharp.Api.Services;

public class ClusterYarpService
{
    private readonly ClusterOverviewService _overviewService;
    private readonly ILogger<ClusterYarpService> _logger;
    private readonly InMemoryConfigProvider _proxyConfigProvider;
    private readonly ConcurrentDictionary<string, DestinationConfig> _destinationCache = new();
    
    private readonly ConcurrentDictionary<string, bool> _destinationHealth = new();

    public ClusterYarpService(
        ClusterOverviewService overviewService,
        InMemoryConfigProvider proxyConfigProvider,
        ILogger<ClusterYarpService> logger)
    {
        _overviewService = overviewService;
        _proxyConfigProvider = proxyConfigProvider;
        _logger = logger;
        
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
                
                var destinations = new Dictionary<string, DestinationConfig>(container.ContainerOnHostStatsList.Count);
                foreach (var hostStats in container.ContainerOnHostStatsList)
                {
                    var destId = $"{container.Name}-{hostStats.Host}";
                    var destKey = $"{destId}-{container.ExternalPort}";
                    
                    if (_destinationHealth.TryGetValue(destKey, out var isHealthy) && !isHealthy)
                        continue;
                    
                    if (!_destinationCache.TryGetValue(destKey, out var destinationConfig))
                    {
                        destinationConfig = new DestinationConfig
                        {
                            Address = $"http://{hostStats.Host}:{container.ExternalPort}",
                            Health = $"http://{hostStats.Host}:{container.ExternalPort}/health", 
                            Metadata = new Dictionary<string, string>
                            {
                                
                                { "IsActive", "true" },
                                { "Priority", "1" } 
                            }
                        };
                        
                        _destinationCache[destKey] = destinationConfig;
                    }
                    
                    destinations.Add(destId, destinationConfig);
                }

                
                if (destinations.Count == 0)
                    continue;

                
                clusters.Add(new ClusterConfig
                {
                    ClusterId = clusterId,
                    LoadBalancingPolicy = "LeastRequests", 
                    SessionAffinity = new SessionAffinityConfig
                    {
                        Enabled = true,
                        Policy = "Cookie",
                        AffinityKeyName = ".Affinity",
                        Cookie = new SessionAffinityCookieConfig
                        {
                            SameSite = SameSiteMode.Lax
                        }
                    },
                    HealthCheck = new HealthCheckConfig
                    {
                        Active = new ActiveHealthCheckConfig
                        {
                            Enabled = true,
                            Interval = TimeSpan.FromSeconds(1),
                            Timeout = TimeSpan.FromSeconds(1),
                            Policy = "ConsecutiveFailures",
                            Path = "/health"  
                        },
                        Passive = new PassiveHealthCheckConfig
                        {
                            Enabled = true,
                            Policy = "TransportFailureRate",
                            ReactivationPeriod = TimeSpan.FromSeconds(3)
                        }
                    },
                    HttpClient = new HttpClientConfig
                    {
                        MaxConnectionsPerServer = 10000, 
                        EnableMultipleHttp2Connections = false, 
                        DangerousAcceptAnyServerCertificate = true, 
                        SslProtocols = SslProtocols.None, 
                        WebProxy = null,
                        RequestHeaderEncoding = null
                    },
                    Destinations = destinations,
                    Metadata = new Dictionary<string, string>
                    {
                        { "PreferredCluster", "true" }
                    }
                });

                
                var routeConfig = new RouteConfig
                {
                    RouteId = $"route-{container.Name}",
                    ClusterId = clusterId,
                    Match = new RouteMatch
                    {
                        Hosts = new[] { container.Name },
                        Path = "/{**catch-all}"
                    },
                    Transforms = new List<Dictionary<string, string>>
                    {
                        
                        new() { { "RequestHeaderOriginalHost", "true" } }
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "ResponseBuffering", "false" },         
                        { "Timeout", "00:00:05" },                
                        { "AllowResponseBuffering", "false" },    
                        { "MaxRequestBodySize", "1048576" },      
                        { "RateLimitingEnabled", "false" },       
                        { "IsCacheable", "true" },                
                        { "Priority", "1" }                       
                    }
                };
                
                routes.Add(routeConfig);
                
                
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
                        new() { { "PathRemovePrefix", $"/{container.Name}" } },
                        new() { { "RequestHeaderOriginalHost", "true" } }
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "ResponseBuffering", "false" },
                        { "Timeout", "00:00:05" },
                        { "AllowResponseBuffering", "false" },
                        { "MaxRequestBodySize", "1048576" },
                        { "RateLimitingEnabled", "false" },
                        { "IsCacheable", "true" }, 
                        { "Priority", "2" }                       
                    }
                });
            }

            
            _proxyConfigProvider.Update(routes, clusters);
            
            _logger.LogInformation("Updated YARP proxy configuration with {RouteCount} routes and {ClusterCount} clusters", 
                routes.Count, clusters.Count);
            
            
            CleanDestinationCache(overview.Containers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating proxy configuration");
        }
    }
    
    
    public void UpdateDestinationHealth(string destinationKey, bool isHealthy)
    {
        _destinationHealth[destinationKey] = isHealthy;
    }
    
    private void CleanDestinationCache(List<Container> containers)
    {
        
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
        
        
        foreach (var key in _destinationCache.Keys)
        {
            if (!activeKeys.Contains(key))
            {
                _destinationCache.TryRemove(key, out _);
                _destinationHealth.TryRemove(key, out _);
            }
        }
    }
} 