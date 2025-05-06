using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Shared;
using FastEndpoints;

namespace ClusterSharp.Api.Endpoints;

public class DeployEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/cluster/deploy/{replicas}");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Deploy to workers";
            s.Description = "Deploys code to the specified number of worker nodes";
            s.ExampleRequest = new { replicas = 3 };
            s.ResponseExamples[200] = "Deployment successful";
            s.Params["replicas"] = "Number of worker nodes to deploy to";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!GithubHelper.IsValidSource(HttpContext.Request.Host.Value!))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var replicasStr = Route<string>("replicas");
        if (!int.TryParse(replicasStr, out var replicas) || replicas == 0)
        {
            AddError("replicas", "Replicas must be a valid number greater than 0");
            await SendErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var workers = ClusterHelper.GetWorkers();

        if (workers.Count == 0)
        {
            await SendAsync("No workers found", StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (replicas > workers.Count)
        {
            await SendAsync($"Not enough workers available. {workers.Count} available, {replicas} requested.", 
                StatusCodes.Status400BadRequest, ct);
            return;
        }

        workers = workers.Take(replicas).ToList();

        var repo = GithubHelper.GetRepoName(HttpContext.Request.Host.Value!);
        var repo = "";
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