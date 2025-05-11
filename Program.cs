using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Error);
builder.Logging.AddFilter("Yarp.ReverseProxy.Health.ActiveHealthCheckMonitor", LogLevel.Error);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());
builder.Services.AddSingleton<RequestStatsService>();
builder.Services.AddSingleton<ClusterSetupService>();

builder.Services.AddHostedService<MachineMonitorBackgroundService>();
builder.Services.AddHostedService<ContainerMonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();
builder.Services.AddHostedService<RequestStatsBackgroundService>();

builder.Services.AddFastEndpoints(options => {
    options.SourceGeneratorDiscoveredTypes = new List<Type>();
});

var app = builder.Build();

app.UseRouting();

app.UseRequestTracker();

app.UseFastEndpoints(config => {
    config.Serializer.Options.PropertyNamingPolicy = null;
});

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.Run();