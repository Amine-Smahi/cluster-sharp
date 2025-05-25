using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;

const int maxConcurrent = 80000;
const int requestTimeoutSeconds = 5;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds);
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
        PooledConnectionLifetime = TimeSpan.FromSeconds(requestTimeoutSeconds),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
        EnableMultipleHttp2Connections = false,
        MaxConnectionsPerServer = maxConcurrent
    });

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);

app.UseRouting();

app.UseReverseProxy();

app.Run("http://0.0.0.0:80");