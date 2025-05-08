using FastEndpoints;
using ClusterSharp.Api.Models.Overview;
using ClusterSharp.Api.Services;
using Yarp.ReverseProxy.Configuration;

namespace ClusterSharp.Api.Endpoints;

public class RouteHealth
{
    public string ContainerName { get; set; } = null!;
    public int Replicas { get; set; }
    public int HealthyReplicas { get; set; }
    public string HostRoute { get; set; } = null!;
    public string PathRoute { get; set; } = null!;
    public string ClusterId { get; set; } = null!;
    public List<string> Destinations { get; set; } = [];
}

public class YarpHealthEndpoint(
    ClusterOverviewService overviewService, 
    IProxyConfigProvider proxyConfigProvider) : EndpointWithoutRequest<List<RouteHealth>>
{
    public override void Configure()
    {
        Get("/yarp/health");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "YARP routes health";
            s.Description = "Returns the health status of all YARP routes";
            s.Response<List<RouteHealth>>(200, "Health data retrieved successfully");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var overview = overviewService.Overview;
        var proxyConfig = proxyConfigProvider.GetConfig();
        
        var routeHealths = new List<RouteHealth>();
        
        foreach (var container in overview.Containers)
        {
            var hostRouteId = $"route-{container.Name}";
            var pathRouteId = $"path-route-{container.Name}";
            var clusterId = $"cluster-{container.Name}";
            
            var hostRoute = proxyConfig.Routes.FirstOrDefault(r => r.RouteId == hostRouteId);
            var pathRoute = proxyConfig.Routes.FirstOrDefault(r => r.RouteId == pathRouteId);
            var cluster = proxyConfig.Clusters.FirstOrDefault(c => c.ClusterId == clusterId);
            
            if ((hostRoute != null || pathRoute != null) && cluster != null)
            {
                routeHealths.Add(new RouteHealth
                {
                    ContainerName = container.Name,
                    Replicas = container.Replicas,
                    HealthyReplicas = cluster.Destinations?.Count ?? 0,
                    HostRoute = container.Name,
                    PathRoute = $"/{container.Name}/{{**catch-all}}",
                    ClusterId = clusterId,
                    Destinations = cluster.Destinations?.Keys.ToList() ?? []
                });
            }
        }
        
        await SendOkAsync(routeHealths, ct);
    }
} 