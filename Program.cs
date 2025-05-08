using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Runtime;
using ClusterSharp.Api.Extensions;
using ClusterSharp.Api.Services;
using FastEndpoints;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Configure logging to filter out specific YARP warnings
builder.Logging.AddFilter("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Error);
builder.Logging.AddFilter("Yarp.ReverseProxy.Health.ActiveHealthCheckMonitor", LogLevel.Error);

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

// Configure the host to handle application shutdown gracefully
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
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
        PooledConnectionLifetime = TimeSpan.FromMinutes(30),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
        EnableMultipleHttp2Connections = true,
        UseProxy = false,
        ResponseDrainTimeout = TimeSpan.FromMinutes(1),
    })
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(2);
    });

builder.Services.AddReverseProxy()
    .LoadFromMemory(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>())
    .ConfigureHttpClient((context, handler) =>
    {
        if (handler is SocketsHttpHandler socketHandler)
        {
            socketHandler.MaxConnectionsPerServer = 1000;
            socketHandler.EnableMultipleHttp2Connections = true;
            socketHandler.PooledConnectionLifetime = TimeSpan.FromMinutes(30);
            socketHandler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10);
            socketHandler.UseProxy = false;
            socketHandler.ResponseDrainTimeout = TimeSpan.FromMinutes(1);
            
            // Additional configuration to handle cancellations better
            socketHandler.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
            socketHandler.KeepAlivePingTimeout = TimeSpan.FromSeconds(15);
            socketHandler.KeepAlivePingPolicy = System.Net.Http.HttpKeepAlivePingPolicy.WithActiveRequests;
        }
    });

builder.Services.AddFastEndpoints();

var app = builder.Build();

// Register application lifetime events to clean up resources
app.Lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("Application is stopping. Cleaning up SSH connections...");
    SshHelper.CleanupConnections();
});

app.UseRouting();

app.UseRequestTimeouts();
app.UseResponseCompression();
app.UseResponseCaching();

app.UseFastEndpoints();

app.UseYarpExceptionHandling();
app.UseSecurityHeaders();

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

var overviewService = app.Services.GetRequiredService<ClusterOverviewService>();
YarpHelper.SetupYarpRouteUpdates(app, overviewService);

app.MapReverseProxy();

app.Run("http://0.0.0.0:80");