using ClusterSharp.Api.Helpers;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace ClusterSharp.Api.Endpoints;

public class DeployRequest
{
    public int Replicas { get; set; }
}

public class DeployEndpoint : Endpoint<DeployRequest>
{
    public override void Configure()
    {
        Get("/cluster/deploy/{Replicas}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DeployRequest req, CancellationToken ct)
    {
        if (!GithubHelper.IsValidSource(HttpContext.Request.Host.Value!))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        if (req.Replicas == 0)
        {
            AddError(nameof(req.Replicas), "Replicas must be greater than 0");
            await SendErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var workers = ClusterHelper.GetWorkers();

        if (workers.Count == 0)
        {
            await SendAsync("No workers found", StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (req.Replicas > workers.Count)
        {
            await SendAsync($"Not enough workers available. {workers.Count} available, {req.Replicas} requested.", 
                StatusCodes.Status400BadRequest, ct);
            return;
        }

        workers = workers.Take(req.Replicas).ToList();

        var repo = GithubHelper.GetRepoName(HttpContext.Request.Host.Value!);
        foreach (var worker in workers)
        {
            var results = SshHelper.ExecuteCommands(worker, CommandHelper.GetDeploymentCommands(repo));
            if (results?.Any(x => x.Status == Constants.NotOk) ?? true)
            {
                await SendAsync(results, StatusCodes.Status400BadRequest, ct);
                return;
            }
        }

        await SendOkAsync(ct);
    }
} 