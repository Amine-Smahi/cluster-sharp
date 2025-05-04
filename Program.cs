using ClusterSharp.Api;
using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<MonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

var app = builder.Build();

app.MapOpenApi();

app.UseHttpsRedirection();

app.MapGet("/cluster/sync", (HttpContext ctx) =>
{
    if(!GithubHelper.IsValidSource(ctx.Request.Host.Value!))
        return Results.Unauthorized();
        
    var results = SshHelper.ExecuteControllerCommands(CommandHelper.GetClusterUpdateCommands());
    return results?.Any(x => x.Status == Constants.NotOk) ?? true
        ? Results.BadRequest(results)
        : Results.Ok(results);
}).WithName("sync");

app.MapGet("/cluster/deploy/{replicas:int}", (HttpContext ctx, int replicas) =>
{
    if(!GithubHelper.IsValidSource(ctx.Request.Host.Value!))
        return Results.Unauthorized();
        
    if(replicas == 0)
        Results.BadRequest("Replicas must be greater than 0");
        
    var workers = ClusterHelper.GetWorkers();
        
    if (workers.Count == 0)
        return Results.BadRequest("No workers found");
        
    if (replicas > workers.Count)
        return Results.BadRequest($"Not enough workers available. {workers.Count} available, {replicas} requested.");
        
    workers = workers.Take(replicas).ToList();

    var repo = GithubHelper.GetRepoName(ctx.Request.Host.Value!);
    foreach (var worker in workers)
    {
        var results = SshHelper.ExecuteCommands(worker, CommandHelper.GetDeploymentCommands(repo));
        if (results?.Any(x => x.Status == Constants.NotOk) ?? true)
            return Results.BadRequest(results);
    }
    return Results.Ok();
}).WithName("deploy");

app.UseSwagger();
app.UseSwaggerUI();
app.Run("http://0.0.0.0:80");