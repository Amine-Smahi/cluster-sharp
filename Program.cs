using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Helpers;
using System.Runtime;
using ClusterSharp.Api.Extensions;
using ClusterSharp.Api.Services;
using FastEndpoints;

var builder = WebApplication.CreateBuilder(args);

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

ThreadPool.SetMinThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 4);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());

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

app.UseYarpExceptionHandling();
app.UseSecurityHeaders();

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

var overviewService = app.Services.GetRequiredService<ClusterOverviewService>();

app.MapReverseProxy();

app.Run("http://0.0.0.0:80");