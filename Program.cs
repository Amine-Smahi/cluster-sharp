using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;
using System.Runtime;
using ClusterSharp.Api.Services;
using FastEndpoints;
using Yarp.ReverseProxy.Configuration;
using ClusterSharp.Api.Middleware;
using System.Net.Http;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using System.Collections.Generic;


var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    
    WebRootPath = "Assets"
});

builder.Logging.AddFilter("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Error);
builder.Logging.AddFilter("Yarp.ReverseProxy.Health.ActiveHealthCheckMonitor", LogLevel.Error);


GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;


ThreadPool.SetMinThreads(Environment.ProcessorCount * 50, Environment.ProcessorCount * 50);
ThreadPool.SetMaxThreads(Environment.ProcessorCount * 200, Environment.ProcessorCount * 200);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());
builder.Services.AddSingleton<RequestStatsService>();


builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 100000;
    options.Limits.MaxConcurrentUpgradedConnections = 100000;
    options.Limits.MaxRequestBodySize = 30 * 1024;
    options.Limits.MinRequestBodyDataRate = null;
    options.Limits.MinResponseDataRate = null;
    options.AddServerHeader = false;
    options.UseSystemd();
    options.ListenAnyIP(80, listenOptions => 
    {
        
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
        listenOptions.UseConnectionLogging();
    });
    options.ConfigureEndpointDefaults(lo => {
        lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });
});


var proxyConfigProvider = new InMemoryConfigProvider(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>());
builder.Services.AddSingleton<IProxyConfigProvider>(proxyConfigProvider);
builder.Services.AddSingleton(proxyConfigProvider);
builder.Services.AddSingleton<ClusterYarpService>();
builder.Services.AddReverseProxy()
    .ConfigureHttpClient((context, handler) => 
    {
        handler.AllowAutoRedirect = false;
        handler.UseCookies = false;
        handler.AutomaticDecompression = System.Net.DecompressionMethods.All;
        if (handler is SocketsHttpHandler socketsHandler)
        {
            socketsHandler.PooledConnectionLifetime = TimeSpan.FromMinutes(30);
            socketsHandler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(15);
            socketsHandler.ConnectTimeout = TimeSpan.FromSeconds(2);
            socketsHandler.MaxConnectionsPerServer = 1000;
            socketsHandler.KeepAlivePingTimeout = TimeSpan.FromSeconds(5);
            socketsHandler.KeepAlivePingDelay = TimeSpan.FromSeconds(10);
            socketsHandler.EnableMultipleHttp2Connections = true;
        }
    });


builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = false;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.AddResponseCaching();
builder.Services.AddRequestTimeouts();
builder.Services.AddOutputCache(options => {
    options.DefaultExpirationTimeSpan = TimeSpan.FromSeconds(10);
    options.SizeLimit = 50 * 1024 * 1024;
    options.MaximumBodySize = 20 * 1024; 
});


builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 50 * 1024 * 1024;
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
});


builder.Services.AddDistributedMemoryCache(options =>
{
    options.SizeLimit = 100 * 1024 * 1024;
    options.CompactionPercentage = 0.2;    
});

builder.Services.AddHostedService<MachineMonitorBackgroundService>();
builder.Services.AddHostedService<ContainerMonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();
builder.Services.AddHostedService<RequestStatsBackgroundService>();

builder.Services.AddFastEndpoints(options => {
    options.SourceGeneratorDiscoveredTypes = new List<Type>();
});

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("Application is stopping. Cleaning up SSH connections...");
    SshHelper.CleanupConnections();
});


app.UseRouting();

app.UseRequestTracker();

app.UseRequestTimeouts();
app.UseResponseCompression();
app.UseResponseCaching();
app.UseOutputCache();

app.UseFastEndpoints(config => {
    config.Serializer.Options.PropertyNamingPolicy = null;
});

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

var yarpService = app.Services.GetRequiredService<ClusterYarpService>();
yarpService.UpdateProxyConfig();

app.MapReverseProxy();

app.Lifetime.ApplicationStarted.Register(() => {
    
    Console.WriteLine($"Server running on port 80");
    Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
    Console.WriteLine($"GC Mode: {GCSettings.LatencyMode}");

    ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
    Console.WriteLine($"ThreadPool Min Threads: Worker={workerThreads}, Completion={completionPortThreads}");
});


app.Run();