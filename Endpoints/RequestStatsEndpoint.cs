using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Stats;
using FastEndpoints;

namespace ClusterSharp.Api.Endpoints;

public class RequestStatsEndpoint : EndpointWithoutRequest<RequestStats>
{
    public override void Configure()
    {
        Get("/stats/requests");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Get latest request statistics";
            s.Description = "Returns the latest request per second statistics";
            s.Response<RequestStats>(200, "Request statistics retrieved successfully");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var stats = YarpHelper.GetLatestRequestStats();
        await SendOkAsync(stats, ct);
    }
} 