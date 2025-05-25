using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;

const int maxConcurrent = 50000;
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(1);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(1);
    options.AllowSynchronousIO = false;
    options.AddServerHeader = false;
    options.Limits.MaxConcurrentConnections = maxConcurrent;
    options.Limits.MaxConcurrentUpgradedConnections = maxConcurrent;
});

builder.Services.AddSingleton(_ => new ClusterOverviewService());
builder.Services.AddSingleton<ClusterSetupService>();

builder.Services.AddHostedService<MachineMonitorBackgroundService>();
builder.Services.AddHostedService<ContainerMonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

builder.Services.AddFastEndpoints(options => options.SourceGeneratorDiscoveredTypes = []);

builder.Services.AddHttpClient("ReverseProxyClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromSeconds(1),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(1),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
        EnableMultipleHttp2Connections = true,
        MaxConnectionsPerServer = maxConcurrent
    });

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);

app.UseRouting();

app.UseReverseProxy();

app.Run("http://0.0.0.0:80");