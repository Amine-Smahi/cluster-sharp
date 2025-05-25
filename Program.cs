using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;

const int maxConcurrent = 80000;
const int requestTimeoutSeconds = 500;

var builder = WebApplication.CreateBuilder(args);
const int workerThreads = 16 * 4;
const int ioThreads = 16 * 4;
const int completionPortThreads = 16 * 8;

ThreadPool.SetMinThreads(workerThreads, ioThreads);
ThreadPool.SetMaxThreads(workerThreads * 16, completionPortThreads * 16);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(requestTimeoutSeconds);
    options.AllowSynchronousIO = false;
    options.AddServerHeader = false;
    options.Limits.MaxConcurrentConnections = maxConcurrent;
    options.Limits.MaxConcurrentUpgradedConnections = maxConcurrent;
    options.Limits.MinResponseDataRate = null;
    options.Limits.MinRequestBodyDataRate = null;
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
        MaxConnectionsPerServer = maxConcurrent,
        UseCookies = false,
        UseProxy = false
    });

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);

app.UseRouting();

app.UseReverseProxy();

app.Run("http://0.0.0.0:80");