using ClusterSharp.Api.Services;
using FastEndpoints;

namespace ClusterSharp.Api.Endpoints;

public class UpdateClusterSetupEndpoint(ClusterSetupService clusterSetupService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/update-cluster-setup");
        AllowAnonymous();
    }
    
    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await clusterSetupService.UpdateClusterSetupAsync();
        
        if (result)
        {
            await SendOkAsync(ct);
        }
        else
        {
            await SendErrorsAsync(StatusCodes.Status500InternalServerError, ct);
        }
    }
} 