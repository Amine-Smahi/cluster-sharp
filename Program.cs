using ClusterSharp.Api;
using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Runtime;
using ClusterSharp.Api.Models.Cluster;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using ClusterSharp.Api.Models;
using System.Text;

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

builder.Services.Configure<BrotliCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; });

builder.Services.Configure<GzipCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; });

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
    if (!GithubHelper.IsValidSource(ctx.Request.Host.Value!))
        return Results.Unauthorized();

    var results = SshHelper.ExecuteControllerCommands(CommandHelper.GetClusterUpdateCommands());
    return results?.Any(x => x.Status == Constants.NotOk) ?? true
        ? Results.BadRequest(results)
        : Results.Ok(results);
}).WithName("sync");

app.MapGet("/cluster/deploy/{replicas:int}", (HttpContext ctx, int replicas) =>
{
    if (!GithubHelper.IsValidSource(ctx.Request.Host.Value!))
        return Results.Unauthorized();

    if (replicas == 0)
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

app.MapGet("/dashboard", (ClusterOverviewService overviewService, HttpContext context) =>
{
    // Set content type to HTML
    context.Response.ContentType = "text/html";

    string dashboardHtml = $@"
<!DOCTYPE html>
<html lang=""en"" data-bs-theme=""dark"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>ClusterSharp Dashboard</title>
    <link href=""https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css"" rel=""stylesheet"">
    <script src=""https://unpkg.com/htmx.org@1.9.10""></script>
    <style>
        .progress-bar {{ transition: width 0.5s ease-in-out; }}
        .small-note {{ font-size: 0.8rem; color: #6c757d; }}
    </style>
</head>
<body>
    <div class=""container mt-4"">
        <div id=""dashboard-content""
             hx-get=""/dashboard/content""
             hx-trigger=""load, every 1s""
             hx-swap=""innerHTML"">
            <div class=""d-flex justify-content-center"">
                <div class=""spinner-border text-primary"" role=""status"">
                    <span class=""visually-hidden"">Loading...</span>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";

    return Results.Text(dashboardHtml, "text/html");
}).WithName("dashboard");

app.MapGet("/dashboard/content", (ClusterOverviewService overviewService) =>
{
    var overview = overviewService.Overview;

    if (!overview.Any())
    {
        return Results.Text("<div class=\"alert alert-warning\">No cluster data available</div>", "text/html");
    }

    var sb = new StringBuilder();
    foreach (var service in overview)
    {
        sb.Append($@"
<div class=""card shadow-sm mb-4 bg-body-tertiary"">
    <div class=""card-header bg-primary bg-gradient text-white"">
        <div class=""d-flex justify-content-between align-items-center"">
            <h3 class=""mb-0"">{service.Name}</h3>
            <span class=""badge bg-light text-dark"">Replicas: {service.Replicas}</span>
        </div>
    </div>
    <div class=""card-body"">
        <div class=""row"">");

        foreach (var host in service.Hosts)
        {
            sb.Append($@"
            <div class=""col-md-6 mb-3"">
                <div class=""card h-100 border-0 shadow-sm"">
                    <div class=""card-body"">
                        <h5 class=""card-title"">{host.Hostname}</h5>
                        <div class=""d-flex justify-content-between mb-2"">
                            <span>External Port:</span>
                            <span class=""fw-bold"">{host.ExternalPort}</span>
                        </div>
                        <div class=""mb-3"">
                            <label class=""form-label d-flex justify-content-between mb-1"">
                                <span>CPU: {host.CPU}%</span>
                            </label>
                            <div class=""progress"" style=""height: 10px;"">
                                <div class=""progress-bar bg-info"" role=""progressbar"" 
                                    style=""width: {Math.Min((int)host.CPU, 100)}%;"" 
                                    aria-valuenow=""{host.CPU}"" aria-valuemin=""0"" aria-valuemax=""100"">
                                </div>
                            </div>
                        </div>
                        <div>
                            <label class=""form-label d-flex justify-content-between mb-1"">
                                <span>Memory: {host.MemoryPercent}%</span>
                            </label>
                            <div class=""progress"" style=""height: 10px;"">
                                <div class=""progress-bar bg-warning"" role=""progressbar"" 
                                    style=""width: {Math.Min((int)host.MemoryPercent, 100)}%;"" 
                                    aria-valuenow=""{host.MemoryPercent}"" aria-valuemin=""0"" aria-valuemax=""100"">
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>");
        }

        sb.Append(@"
        </div>
    </div>
</div>");
    }

    return Results.Text(sb.ToString(), "text/html");
}).WithName("dashboard-content");

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

var overviewService = app.Services.GetRequiredService<ClusterOverviewService>();
YarpHelper.SetupYarpRouteUpdates(app, overviewService);

app.MapReverseProxy();

app.Run("http://0.0.0.0:80");