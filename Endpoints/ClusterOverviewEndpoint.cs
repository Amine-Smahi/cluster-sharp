using FastEndpoints;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models;

namespace ClusterSharp.Api.Endpoints;

public class ClusterOverviewEndpoint : EndpointWithoutRequest
{
    private readonly ClusterOverviewService _overviewService;

    public ClusterOverviewEndpoint(ClusterOverviewService overviewService)
    {
        _overviewService = overviewService;
    }

    public override void Configure()
    {
        Get("/cluster/overview");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Cluster overview endpoint";
            s.Description = "Returns the cluster overview information";
            s.Response<ClusterOverview>(200, "Cluster overview retrieved successfully");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var overview = _overviewService.Overview;
        await SendOkAsync(overview, ct);
    }
} 