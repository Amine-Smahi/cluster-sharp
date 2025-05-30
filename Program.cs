using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;

var builder = WebApplication.CreateBuilder(args);
const int workerThreads = 16 * 4;
const int ioThreads = 16 * 4;
const int completionPortThreads = 16 * 8;

ThreadPool.SetMinThreads(workerThreads, ioThreads);
ThreadPool.SetMaxThreads(workerThreads * 32, completionPortThreads * 32);

builder.Services.AddSingleton(_ => new ClusterOverviewService());
builder.Services.AddSingleton<ClusterSetupService>();

builder.Services.AddHostedService<MachineMonitorBackgroundService>();
builder.Services.AddHostedService<ContainerMonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

builder.Services.AddFastEndpoints(options => options.SourceGeneratorDiscoveredTypes = []);

builder.Services.AddReverseProxy();

const int forwarderRequestTimeoutSeconds = 120;
const int maxConnectionsPerServer = 80000;

builder.Services.AddHttpClient("YarpForwarderClient", client =>
{
    // Optional: configure default request headers or other client-wide settings here
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromSeconds(forwarderRequestTimeoutSeconds),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(forwarderRequestTimeoutSeconds),
    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
    EnableMultipleHttp2Connections = false,
    MaxConnectionsPerServer = maxConnectionsPerServer,
    UseCookies = false,
    UseProxy = false
});

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);

app.UseRouting();
app.UseFastEndpoints();

app.Run("http://0.0.0.0:80");