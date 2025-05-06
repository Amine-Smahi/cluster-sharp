using ClusterSharp.Api.Services;
using Yarp.ReverseProxy.Configuration;

namespace ClusterSharp.Api.Helpers;

public static class YarpHelper
{
    private static WebApplication? _app;
    private static readonly SemaphoreSlim UpdateLock = new(1, 1);
    private static DateTime _lastUpdateTime = DateTime.MinValue;
    private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromSeconds(5);
    private static bool _updatePending;

    public static void SetupYarpRouteUpdates(WebApplication app, ClusterOverviewService overviewService)
    {
        _app = app;
        overviewService.OverviewUpdated += OnOverviewUpdated;
        UpdateYarpRoutes(app);
    }
    
    private static void OnOverviewUpdated(object? sender, EventArgs e)
    {
        if (_app == null) return;
        
        if (!UpdateLock.Wait(0))
        {
            _updatePending = true;
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            if (now - _lastUpdateTime < MinUpdateInterval)
            {
                _updatePending = true;
                Task.Delay(MinUpdateInterval - (now - _lastUpdateTime))
                    .ContinueWith(_ => CheckAndProcessPendingUpdate());
                return;
            }

            UpdateYarpRoutesInternal(_app);
            _lastUpdateTime = DateTime.UtcNow;
            _updatePending = false;
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    private static void CheckAndProcessPendingUpdate()
    {
        if (!_updatePending || _app == null) return;

        try
        {
            UpdateLock.Wait();
            if (_updatePending)
            {
                UpdateYarpRoutesInternal(_app);
                _lastUpdateTime = DateTime.UtcNow;
                _updatePending = false;
                Console.WriteLine($"Processed pending YARP routes update at {DateTime.UtcNow:HH:mm:ss}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in pending YARP update: {ex.Message}");
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    private static void UpdateYarpRoutes(WebApplication application)
    {
        if (!UpdateLock.Wait(0))
        {
            _updatePending = true;
            return;
        }

        try
        {
            UpdateYarpRoutesInternal(application);
            _lastUpdateTime = DateTime.UtcNow;
            _updatePending = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating YARP routes: {ex.Message}");
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    private static void UpdateYarpRoutesInternal(WebApplication application)
    {
        var overviewService = application.Services.GetRequiredService<ClusterOverviewService>();
        var containers = overviewService.Overview.Containers;
        var routeConfigs = new List<RouteConfig>(containers.Count);
        var clusterConfigs = new List<ClusterConfig>(containers.Count);

        foreach (var container in containers)
        {
            if (container.Hosts.Count == 0)
                continue;

            routeConfigs.Add(new RouteConfig
            {
                RouteId = container.Name,
                ClusterId = container.Name,
                Match = new RouteMatch
                {
                    Hosts = [container.Name],
                    Path = "/{**catchall}"
                },
                Transforms = new List<Dictionary<string, string>>
                {
                    new() { { "ResponseHeader", "Cache-Control" }, { "Append", "public, max-age=120" } }
                },
                Timeout = TimeSpan.FromSeconds(30)
            });

            var destinationCapacity = container.Hosts.Count;
            var destinations = new Dictionary<string, DestinationConfig>(destinationCapacity);

            for (var i = 0; i < container.Hosts.Count; i++)
            {
                var host = container.Hosts[i];
                destinations.Add($"{container.Name}-{i}", new DestinationConfig
                {
                    Address = $"http://{host}:{container.ExternalPort}",
                    Health = $"http://{host}:{container.ExternalPort}/health",
                    Metadata = new Dictionary<string, string>
                    {
                        { "MaxConcurrentRequests", "200" }
                    }
                });
            }

            clusterConfigs.Add(new ClusterConfig
            {
                ClusterId = container.Name,
                Destinations = destinations,
                LoadBalancingPolicy = "RoundRobin",
                HealthCheck = new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig
                    {
                        Enabled = true,
                        Interval = TimeSpan.FromSeconds(10),
                        Timeout = TimeSpan.FromSeconds(5),
                        Policy = "ConsecutiveFailures"
                    }
                },
                HttpClient = new HttpClientConfig
                {
                    MaxConnectionsPerServer = 100,
                    DangerousAcceptAnyServerCertificate = false,
                    RequestHeaderEncoding = "utf-8"
                }
            });
        }

        try
        {
            if (application.Services.GetRequiredService<IProxyConfigProvider>() is InMemoryConfigProvider proxyConfigProvider)
            {
                var currentConfig = proxyConfigProvider.GetConfig();
                
                if (AreRoutesEqual(currentConfig.Routes, routeConfigs) && 
                    AreClustersEqual(currentConfig.Clusters, clusterConfigs)) return;
                
                proxyConfigProvider.Update(routeConfigs, clusterConfigs);
                Console.WriteLine($"Yarp routes updated at {DateTime.UtcNow:HH:mm:ss} for {routeConfigs.Count} apps");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update YARP routes: {ex.Message}");
        }
    }
    
    private static bool AreRoutesEqual(IReadOnlyList<RouteConfig> existingRoutes, IReadOnlyList<RouteConfig> newRoutes)
    {
        if (existingRoutes.Count != newRoutes.Count)
            return false;
        
        var existingRouteMap = existingRoutes.ToDictionary(r => r.RouteId);
        
        foreach (var newRoute in newRoutes)
        {
            if (!existingRouteMap.TryGetValue(newRoute.RouteId, out var existingRoute))
                return false;
                
            if (existingRoute.ClusterId != newRoute.ClusterId)
                return false;
                
            if (!AreMatchesEqual(existingRoute.Match, newRoute.Match))
                return false;
                
            if (existingRoute.Timeout != newRoute.Timeout)
                return false;
                
            if (!AreTransformsEqual(existingRoute.Transforms!, newRoute.Transforms!))
                return false;
        }
        
        return true;
    }
    
    private static bool AreMatchesEqual(RouteMatch? existing, RouteMatch? @new)
    {
        if (existing == null && @new == null)
            return true;
            
        if (existing == null || @new == null)
            return false;
            
        if (existing.Path != @new.Path)
            return false;
            
        if (!AreListsEqual(existing.Hosts, @new.Hosts))
            return false;
            
        return true;
    }
    
    private static bool AreTransformsEqual(IReadOnlyList<IReadOnlyDictionary<string, string>> existing, 
                                         IReadOnlyList<IReadOnlyDictionary<string, string>> @new)
    {
        if (existing.Count != @new.Count)
            return false;
            
        for (int i = 0; i < existing.Count; i++)
        {
            var existingDict = existing[i];
            var newDict = @new[i];
            
            if (existingDict.Count != newDict.Count)
                return false;
                
            foreach (var kvp in existingDict)
            {
                if (!newDict.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                    return false;
            }
        }
        
        return true;
    }
    
    private static bool AreClustersEqual(IReadOnlyList<ClusterConfig> existingClusters, IReadOnlyList<ClusterConfig> newClusters)
    {
        if (existingClusters.Count != newClusters.Count)
            return false;
            
        var existingClusterMap = existingClusters.ToDictionary(c => c.ClusterId);
        
        foreach (var newCluster in newClusters)
        {
            if (!existingClusterMap.TryGetValue(newCluster.ClusterId, out var existingCluster))
                return false;
                
            if (existingCluster.LoadBalancingPolicy != newCluster.LoadBalancingPolicy)
                return false;
                
            if (!AreDestinationsEqual(existingCluster.Destinations!, newCluster.Destinations!))
                return false;
        }
        
        return true;
    }
    
    private static bool AreDestinationsEqual(IReadOnlyDictionary<string, DestinationConfig> existing, 
                                            IReadOnlyDictionary<string, DestinationConfig> @new)
    {
        if (existing.Count != @new.Count)
            return false;
            
        foreach (var key in existing.Keys)
        {
            if (!@new.TryGetValue(key, out var newDestination))
                return false;
                
            var existingDestination = existing[key];
            
            if (existingDestination.Address != newDestination.Address)
                return false;
                
            if (existingDestination.Health != newDestination.Health)
                return false;
        }
        
        return true;
    }
    
    private static bool AreListsEqual<T>(IReadOnlyList<T>? list1, IReadOnlyList<T>? list2)
    {
        if (list1 == null && list2 == null)
            return true;
            
        if (list1 == null || list2 == null)
            return false;
            
        if (list1.Count != list2.Count)
            return false;

        return !list1.Where((t, i) => !EqualityComparer<T>.Default.Equals(t, list2[i])).Any();
    }
} 