using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Services;
using FastEndpoints;

namespace ClusterSharp.Api.Endpoints;

public class RequestStatsEndpoint(RequestStatsService requestStatsService) : EndpointWithoutRequest<RequestStats>
{
    public override void Configure()
    {
        Get("/stats/requests");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Get request statistics";
            s.Description = "Returns the current request statistics including requests per second";
            s.Response<RequestStats>(200, "Current request statistics");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var stats = requestStatsService.GetCurrentStats();
        await SendOkAsync(stats, ct);
    }
} 