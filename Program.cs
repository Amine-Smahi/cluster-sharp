using ClusterSharp.Api;
using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Runtime;
using ClusterSharp.Api.Models.Cluster;
using System.Text;
using System.Globalization;

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
    try
    {
        await next.Invoke();
    }
    catch (Exception ex) when (ex.Message.Contains("No destination available for") ||
                               ex.Message.Contains("Failed to resolve destination"))
    {
        await Task.Delay(100);
        try
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            await next.Invoke();
        }
        catch
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("Service temporarily unavailable. Please try again.");
        }
    }
});

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

app.MapGet("/dashboard", (ClusterOverviewService overviewService) =>
{
    var overview = overviewService.Overview;

    var html = $@"
<!DOCTYPE html>
<html lang='en' data-bs-theme=""dark"">
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>ClusterSharp Dashboard</title>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css' rel='stylesheet'>
    <script src='https://unpkg.com/htmx.org@1.9.10'></script>
    <style>
        .card {{
            transition: all 0.3s;
        }}
        .card:hover {{
            transform: translateY(-5px);
            box-shadow: 0 10px 20px rgba(0,0,0,0.1);
        }}
        .container-card {{
            border-left: 4px solid #0d6efd;
        }}
        .machine-card {{
            border-left: 4px solid #198754;
        }}
        .timestamp {{
            font-size: 0.8rem;
            color: #6c757d;
        }}
        progress {{
            width: 100%;
            height: 20px;
            border-radius: 0.25rem;
            overflow: hidden;
        }}
        progress::-webkit-progress-bar {{
            background-color: #e9ecef;
        }}
        progress::-webkit-progress-value {{
            transition: width 0.3s ease;
        }}
        progress.bg-success::-webkit-progress-value {{
            background-color: #198754;
        }}
        progress.bg-warning::-webkit-progress-value {{
            background-color: #ffc107;
        }}
        progress.bg-danger::-webkit-progress-value {{
            background-color: #dc3545;
        }}
        progress.bg-success::-moz-progress-bar {{
            background-color: #198754;
        }}
        progress.bg-warning::-moz-progress-bar {{
            background-color: #ffc107;
        }}
        progress.bg-danger::-moz-progress-bar {{
            background-color: #dc3545;
        }}
        .progress-label {{
            margin-top: 5px;
            text-align: center;
        }}
    </style>
</head>
<body class='h-100 p-3 fs-6' hx-get='/dashboard' hx-trigger='every 5s' hx-swap='outerHTML'>        
        <div class='row mb-3'>
            <div class='col-12 mb-3'>
                <h2>Machines</h2>
            </div>
            {(overview?.Machines != null ? string.Join("", overview.Machines.Select(machine => $@"
            <div class='col-xl-3 col-lg-4 col-md-6 col-sm-6 mb-3'>
                <div class='card bg-dark-subtle shadow-sm machine-card'>
                    <div class='card-body'>
                        <h5 class='card-title'>{machine.Hostname}</h5>
                        <div class='row'>
                            <div class='col-6'>
                                <p class='mb-1'>CPU</p>
                                <progress class='bg-{GetColorClass(machine.CpuPercent)}' 
                                          value='{machine.CpuPercent.ToString(CultureInfo.InvariantCulture)}' max='100'></progress>
                                <p class='progress-label'>{machine.CpuPercent.ToString(CultureInfo.InvariantCulture)}%</p>
                            </div>
                            <div class='col-6'>
                                <p class='mb-1'>Memory</p>
                                <progress class='bg-{GetColorClass(machine.MemoryPercent)}' 
                                          value='{machine.MemoryPercent.ToString(CultureInfo.InvariantCulture)}' max='100'></progress>
                                <p class='progress-label'>{machine.MemoryPercent.ToString(CultureInfo.InvariantCulture)}%</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>")) : "<div class='col-12'><div class='alert alert-warning'>No machine data available</div></div>")}
        </div>
        
        <div class='row mb-3'>
            <div class='col-12 mb-3'>
                <h2>Containers</h2>
            </div>
            {(overview?.Containers != null ? string.Join("", overview.Containers.Select(container => $@"
            <div class='col-xl-3 col-lg-4 col-md-6 col-sm-6 mb-3'>
                <div class='card bg-dark-subtle shadow-sm container-card'>
                    <div class='card-body'>
                        <h5 class='card-title'>{container.Name}</h5>
                        <div class='d-flex justify-content-between mb-2'>
                            <span class='badge bg-primary'>{container.Replicas} {(container.Replicas == 1 ? "replica" : "replicas")}</span>
                            {(string.IsNullOrEmpty(container.ExternalPort) ? "" : $"<span class='badge bg-success'>Port: {container.ExternalPort}</span>")}
                        </div>
                        <p class='card-text mb-1'>Hosts:</p>
                            {string.Join("", container.Hosts.Select(host => $"<span class=\"badge rounded-pill text-bg-secondary me-2\">{host}</span>"))}
                    </div>
                </div>
            </div>")) : "<div class='col-12'><div class='alert alert-warning'>No container data available</div></div>")}
        </div>
    </div>
</body>
</html>";

    return Results.Content(html, "text/html", Encoding.UTF8);
}).WithName("dashboard");

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();


app.MapGet("/health", (IProxyConfigProvider configProvider) =>
{
    var config = configProvider.GetConfig();
    var health = new
    {
        Status = "healthy",
        RouteCount = config.Routes.Count,
        ClusterCount = config.Clusters.Count,
        Timestamp = DateTime.UtcNow
    };
    return Results.Ok(health);
}).WithName("health");

var overviewService = app.Services.GetRequiredService<ClusterOverviewService>();
YarpHelper.SetupYarpRouteUpdates(app, overviewService);

app.MapReverseProxy();

app.Run("http://0.0.0.0:80");
return;

string GetColorClass(double percent)
{
    return percent switch
    {
        < 50 => "success",
        < 80 => "warning",
        _ => "danger"
    };
}