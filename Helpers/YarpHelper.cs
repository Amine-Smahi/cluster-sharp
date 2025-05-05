using ClusterSharp.Api.Models.Cluster;
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
            var proxyConfigProvider = application.Services.GetRequiredService<IProxyConfigProvider>() as InMemoryConfigProvider;
            proxyConfigProvider?.Update(routeConfigs, clusterConfigs);
            Console.WriteLine($"Yarp routes updated at {DateTime.UtcNow:HH:mm:ss} with {routeConfigs.Count} routes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update YARP routes: {ex.Message}");
        }
    }
} 