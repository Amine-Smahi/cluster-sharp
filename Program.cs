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
using FastEndpoints;

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

// Add FastEndpoints
builder.Services.AddFastEndpoints();

var app = builder.Build();

app.UseRouting();

app.UseRequestTimeouts();
app.UseResponseCompression();
app.UseResponseCaching();

app.UseFastEndpoints();

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

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

var overviewService = app.Services.GetRequiredService<ClusterOverviewService>();
YarpHelper.SetupYarpRouteUpdates(app, overviewService);

app.MapReverseProxy();

app.Run("http://0.0.0.0:80");
return;