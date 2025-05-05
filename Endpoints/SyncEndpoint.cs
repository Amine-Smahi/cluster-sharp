using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models;
using FastEndpoints;

namespace ClusterSharp.Api.Endpoints;

public class SyncResponse
{
    public List<CommandResult>? Results { get; set; }
} 

public class SyncEndpoint : EndpointWithoutRequest<SyncResponse>
{
    public override void Configure()
    {
        Get("/cluster/sync");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Synchronize the cluster by executing update commands";
            s.Description = "Executes cluster update commands on the controller node";
            s.Response<SyncResponse>(200, "Successfully synchronized the cluster");
            s.Response(401, "Unauthorized access");
            s.Response(400, "Failed to synchronize the cluster");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!GithubHelper.IsValidSource(HttpContext.Request.Host.Value!))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var results = SshHelper.ExecuteControllerCommands(CommandHelper.GetClusterUpdateCommands());
        
        if (results?.Any(x => x.Status == Constants.NotOk) ?? true)
        {
            await SendAsync(new SyncResponse { Results = results }, 400, ct);
            return;
        }
        
        await SendOkAsync(new SyncResponse { Results = results }, ct);
    }
} 