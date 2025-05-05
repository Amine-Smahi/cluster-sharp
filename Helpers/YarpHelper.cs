using ClusterSharp.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace ClusterSharp.Api.Helpers;

public static class YarpHelper
{
    public static void UpdateYarpRoutes(WebApplication application)
    {
        var overviewService = application.Services.GetRequiredService<ClusterOverviewService>();
        var routeConfigs = new List<RouteConfig>(overviewService.Overview.Count);
        var clusterConfigs = new List<ClusterConfig>(overviewService.Overview.Count);

        foreach (var overview in overviewService.Overview)
        {
            if (overview.Hosts.Count == 0)
                continue;

            routeConfigs.Add(new RouteConfig
            {
                RouteId = overview.Name,
                ClusterId = overview.Name,
                Match = new RouteMatch
                {
                    Hosts = [overview.Name],
                    Path = "/{**catchall}"
                },
                Transforms = new List<Dictionary<string, string>>
                {
                    new() { { "ResponseHeader", "Cache-Control" }, { "Append", "public, max-age=120" } }
                },
                Timeout = TimeSpan.FromSeconds(30)
            });

            var destinationCapacity = overview.Hosts.Count;
            var destinations = new Dictionary<string, DestinationConfig>(destinationCapacity);

            for (var i = 0; i < overview.Hosts.Count; i++)
            {
                var host = overview.Hosts[i];
                destinations.Add($"{overview.Name}-{i}", new DestinationConfig
                {
                    Address = $"http://{host.Hostname}:{host.ExternalPort}",
                    Health = $"http://{host.Hostname}:{host.ExternalPort}/health",
                    Metadata = new Dictionary<string, string>
                    {
                        { "MaxConcurrentRequests", "200" }
                    }
                });
            }

            clusterConfigs.Add(new ClusterConfig
            {
                ClusterId = overview.Name,
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

        var proxyConfigProvider = application.Services.GetRequiredService<IProxyConfigProvider>() as InMemoryConfigProvider;
        proxyConfigProvider?.Update(routeConfigs, clusterConfigs);
    }
} 