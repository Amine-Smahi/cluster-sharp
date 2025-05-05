using ClusterSharp.Api;
using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Runtime;
using ClusterSharp.Api.Models.Cluster;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = null;
    options.Limits.MaxConcurrentUpgradedConnections = null;
    options.Limits.Http2.MaxStreamsPerConnection = 1000;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.AddServerHeader = false;
});

ThreadPool.SetMinThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 4);

builder.Services.AddRequestTimeouts();

builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024 * 1024 * 100;
    options.CompactionPercentage = 0.2;
});

builder.Services.AddResponseCaching(options =>
{
    options.MaximumBodySize = 64 * 1024;
    options.UseCaseSensitivePaths = false;
    options.SizeLimit = 100 * 1024 * 1024;
});

builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "application/xml", "text/plain"]);
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options => 
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options => 
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());

builder.Services.AddHostedService<MonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

builder.Services.AddHttpClient("YARP")
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 1000,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        EnableMultipleHttp2Connections = true,
        UseProxy = false,
    });

builder.Services.AddReverseProxy()
    .LoadFromMemory(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>())
    .ConfigureHttpClient((context, handler) =>
    {
        if (handler is SocketsHttpHandler socketHandler)
        {
            socketHandler.MaxConnectionsPerServer = 1000;
            socketHandler.EnableMultipleHttp2Connections = true;
            socketHandler.PooledConnectionLifetime = TimeSpan.FromMinutes(10);
            socketHandler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5);
            socketHandler.UseProxy = false;
        }
    });

builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout
    };
});

var app = builder.Build();

app.UseRouting();

app.UseRequestTimeouts();

app.UseResponseCompression();
app.UseResponseCaching();

app.Use(async (context, next) =>
{
    context.Response.Headers.Remove("Server");
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    
    var path = context.Request.Path.ToString().ToLowerInvariant();
    if (path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".jpg") || 
        path.EndsWith(".png") || path.EndsWith(".gif") || path.EndsWith(".woff2"))
    {
        context.Response.Headers["Cache-Control"] = "public,max-age=86400";
    }
    
    await next.Invoke();
});

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
        return Results.BadRequest("Replicas must be greater than 0");
        
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

app.MapGet("/cluster/overview", (ClusterOverviewService overviewService) =>
{
    var overview = overviewService.Overview;
    return Results.Ok(overview);
}).WithName("overview");



app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

var overviewService = app.Services.GetRequiredService<ClusterOverviewService>();
YarpHelper.SetupYarpRouteUpdates(app, overviewService);

app.MapReverseProxy();

app.Run("http://0.0.0.0:80");