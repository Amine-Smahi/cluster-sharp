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
    private static string _lastContainerHash = string.Empty;

    public static DateTime LastUpdateTime => _lastUpdateTime;

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
            var now = DateTime.Now;
            if (now - _lastUpdateTime < MinUpdateInterval)
            {
                _updatePending = true;
                Task.Delay(MinUpdateInterval - (now - _lastUpdateTime))
                    .ContinueWith(_ => CheckAndProcessPendingUpdate());
                return;
            }

            UpdateYarpRoutesInternal(_app);
            _lastUpdateTime = DateTime.Now;
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
                _lastUpdateTime = DateTime.Now;
                _updatePending = false;
                Console.WriteLine($"Processed pending YARP routes update at {DateTime.Now:HH:mm:ss}");
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
            _lastUpdateTime = DateTime.Now;
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
        var currentContainerHash = GenerateContainerConfigHash(containers);
        if (currentContainerHash == _lastContainerHash)
            return;
        
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
                    AreClustersEqual(currentConfig.Clusters, clusterConfigs)) 
                {
                    Console.WriteLine($"YARP configuration is unchanged, skipping update at {DateTime.Now:HH:mm:ss}");
                    return;
                }
                
                proxyConfigProvider.Update(routeConfigs, clusterConfigs);
                _lastContainerHash = currentContainerHash;
                Console.WriteLine($"Yarp routes updated at {DateTime.Now:HH:mm:ss} for {routeConfigs.Count} apps");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update YARP routes: {ex.Message}");
        }
    }
    
    private static string GenerateContainerConfigHash(List<Models.Overview.Container> containers)
    {
        var hash = string.Join("|", 
            containers.OrderBy(c => c.Name).Select(c => 
                $"{c.Name}:{c.ExternalPort}:{string.Join(",", c.Hosts.OrderBy(h => h))}"));
        return hash;
    }
    
    private static bool AreRoutesEqual(IReadOnlyList<RouteConfig> existingRoutes, IReadOnlyList<RouteConfig> newRoutes)
    {
        if (existingRoutes.Count != newRoutes.Count)
        {
            Console.WriteLine($"Route count different: existing={existingRoutes.Count}, new={newRoutes.Count}");
            return false;
        }
        
        var existingRouteMap = existingRoutes.ToDictionary(r => r.RouteId);
        
        foreach (var newRoute in newRoutes)
        {
            if (!existingRouteMap.TryGetValue(newRoute.RouteId, out var existingRoute))
            {
                Console.WriteLine($"Route ID not found: {newRoute.RouteId}");
                return false;
            }
                
            if (existingRoute.ClusterId != newRoute.ClusterId)
            {
                Console.WriteLine($"Cluster ID different for route {newRoute.RouteId}: existing={existingRoute.ClusterId}, new={newRoute.ClusterId}");
                return false;
            }
                
            if (!AreMatchesEqual(existingRoute.Match, newRoute.Match))
            {
                Console.WriteLine($"Matches different for route {newRoute.RouteId}");
                return false;
            }
                
            if (existingRoute.Timeout != newRoute.Timeout)
            {
                Console.WriteLine($"Timeout different for route {newRoute.RouteId}: existing={existingRoute.Timeout}, new={newRoute.Timeout}");
                return false;
            }
                
            if (!AreTransformsEqual(existingRoute.Transforms!, newRoute.Transforms!))
            {
                Console.WriteLine($"Transforms different for route {newRoute.RouteId}");
                return false;
            }
        }
        
        return true;
    }
    
    private static bool AreMatchesEqual(RouteMatch? existing, RouteMatch? @new)
    {
        if (existing == null && @new == null)
            return true;
            
        if (existing == null || @new == null)
        {
            Console.WriteLine("One match is null, the other is not");
            return false;
        }
            
        if (existing.Path != @new.Path)
        {
            Console.WriteLine($"Path different: existing={existing.Path}, new={@new.Path}");
            return false;
        }
            
        if (!AreListsEqual(existing.Hosts, @new.Hosts))
        {
            Console.WriteLine("Hosts different");
            return false;
        }
            
        return true;
    }
    
    private static bool AreTransformsEqual(IReadOnlyList<IReadOnlyDictionary<string, string>> existing, 
                                         IReadOnlyList<IReadOnlyDictionary<string, string>> @new)
    {
        if (existing.Count != @new.Count)
        {
            Console.WriteLine($"Transform count different: existing={existing.Count}, new={@new.Count}");
            return false;
        }
            
        for (int i = 0; i < existing.Count; i++)
        {
            var existingDict = existing[i];
            var newDict = @new[i];
            
            if (existingDict.Count != newDict.Count)
            {
                Console.WriteLine($"Transform dict count different at index {i}: existing={existingDict.Count}, new={newDict.Count}");
                return false;
            }
                
            foreach (var kvp in existingDict)
            {
                if (!newDict.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                {
                    Console.WriteLine($"Transform dict value different for key {kvp.Key}: existing={kvp.Value}, new={value ?? "null"}");
                    return false;
                }
            }
        }
        
        return true;
    }
    
    private static bool AreClustersEqual(IReadOnlyList<ClusterConfig> existingClusters, IReadOnlyList<ClusterConfig> newClusters)
    {
        if (existingClusters.Count != newClusters.Count)
        {
            Console.WriteLine($"Cluster count different: existing={existingClusters.Count}, new={newClusters.Count}");
            return false;
        }
            
        var existingClusterMap = existingClusters.ToDictionary(c => c.ClusterId);
        
        foreach (var newCluster in newClusters)
        {
            if (!existingClusterMap.TryGetValue(newCluster.ClusterId, out var existingCluster))
            {
                Console.WriteLine($"Cluster ID not found: {newCluster.ClusterId}");
                return false;
            }
                
            if (existingCluster.LoadBalancingPolicy != newCluster.LoadBalancingPolicy)
            {
                Console.WriteLine($"LoadBalancingPolicy different for cluster {newCluster.ClusterId}: existing={existingCluster.LoadBalancingPolicy}, new={newCluster.LoadBalancingPolicy}");
                return false;
            }
                
            if (!AreDestinationsEqual(existingCluster.Destinations!, newCluster.Destinations!))
            {
                Console.WriteLine($"Destinations different for cluster {newCluster.ClusterId}");
                return false;
            }
        }
        
        return true;
    }
    
    private static bool AreDestinationsEqual(IReadOnlyDictionary<string, DestinationConfig> existing, 
                                            IReadOnlyDictionary<string, DestinationConfig> @new)
    {
        if (existing.Count != @new.Count)
        {
            Console.WriteLine($"Destination count different: existing={existing.Count}, new={@new.Count}");
            return false;
        }
            
        foreach (var key in existing.Keys)
        {
            if (!@new.TryGetValue(key, out var newDestination))
            {
                Console.WriteLine($"Destination key not found: {key}");
                return false;
            }
                
            var existingDestination = existing[key];
            
            if (existingDestination.Address != newDestination.Address)
            {
                Console.WriteLine($"Destination address different for {key}: existing={existingDestination.Address}, new={newDestination.Address}");
                return false;
            }
                
            if (existingDestination.Health != newDestination.Health)
            {
                Console.WriteLine($"Destination health different for {key}: existing={existingDestination.Health}, new={newDestination.Health}");
                return false;
            }
        }
        
        return true;
    }
    
    private static bool AreListsEqual<T>(IReadOnlyList<T>? list1, IReadOnlyList<T>? list2)
    {
        if (list1 == null && list2 == null)
            return true;
            
        if (list1 == null || list2 == null)
        {
            Console.WriteLine("One list is null, the other is not");
            return false;
        }
            
        if (list1.Count != list2.Count)
        {
            Console.WriteLine($"List count different: list1={list1.Count}, list2={list2.Count}");
            return false;
        }

        for (int i = 0; i < list1.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(list1[i], list2[i]))
            {
                Console.WriteLine($"List item different at index {i}: list1={list1[i]}, list2={list2[i]}");
                return false;
            }
        }
        
        return true;
    }
} 