using FastEndpoints;
using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.Endpoints;

public class RefreshYarpConfigEndpoint(ClusterYarpService yarpService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/yarp/refresh");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Refresh YARP configuration";
            s.Description = "Manually triggers a refresh of the YARP routing configuration";
            s.Response<string>(200, "Configuration refreshed successfully");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        yarpService.UpdateProxyConfig();
        await SendOkAsync("YARP configuration refreshed successfully", ct);
    }
} 