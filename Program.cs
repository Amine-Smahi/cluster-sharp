using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Error);
builder.Logging.AddFilter("Yarp.ReverseProxy.Health.ActiveHealthCheckMonitor", LogLevel.Error);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());
builder.Services.AddSingleton<ClusterSetupService>();
builder.Services.AddSingleton<ProxyRule>();
builder.Services.AddSingleton<ProxyUpdaterService>();

builder.Services.AddHostedService<MachineMonitorBackgroundService>();
builder.Services.AddHostedService<ContainerMonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

builder.Services.AddFastEndpoints(options => {
    options.SourceGeneratorDiscoveredTypes = new List<Type>();
});

var app = builder.Build();

// Initialize the ClusterHelper with the service provider
ClusterHelper.Initialize(app.Services);

// Register to close all SSH connections when application is shutting down
app.Lifetime.ApplicationStopping.Register(() => SshHelper.CloseAllConnections());

app.UseRouting();

app.UseFastEndpoints(config => {
    config.Serializer.Options.PropertyNamingPolicy = null;
});

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.Run();