using FastEndpoints;
using Yarp.ReverseProxy.Configuration;

namespace ClusterSharp.Api.Endpoints.Health.Endpoints;

public class HealthResponse
{
    public string Status { get; set; } = "healthy";
    public int RouteCount { get; set; }
    public int ClusterCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
} 

public class HealthEndpoint : EndpointWithoutRequest<HealthResponse>
{
    private readonly IProxyConfigProvider _configProvider;

    public HealthEndpoint(IProxyConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Health check endpoint";
            s.Description = "Returns the health status of the API and related metrics";
            s.Response<HealthResponse>(200, "API is healthy");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var config = _configProvider.GetConfig();
        
        await SendOkAsync(new HealthResponse
        {
            Status = "healthy",
            RouteCount = config.Routes.Count,
            ClusterCount = config.Clusters.Count,
            Timestamp = DateTime.UtcNow
        }, ct);
    }
} 