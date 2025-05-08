using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;
using System.Runtime;
using ClusterSharp.Api.Services;
using FastEndpoints;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

ThreadPool.SetMinThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 4);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());

// Add YARP services
var proxyConfigProvider = new InMemoryConfigProvider(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>());
builder.Services.AddSingleton<IProxyConfigProvider>(proxyConfigProvider);
builder.Services.AddSingleton(proxyConfigProvider);
builder.Services.AddSingleton<ClusterYarpService>();
builder.Services.AddReverseProxy();

// Add additional required services
builder.Services.AddResponseCompression();
builder.Services.AddResponseCaching();
builder.Services.AddRequestTimeouts();

builder.Services.AddHostedService<MonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

builder.Services.AddFastEndpoints();

var app = builder.Build();

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

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

var overviewService = app.Services.GetRequiredService<ClusterOverviewService>();
var yarpService = app.Services.GetRequiredService<ClusterYarpService>();

// Initialize YARP configuration
yarpService.UpdateProxyConfig();

app.MapReverseProxy();

app.Run("http://0.0.0.0:80");