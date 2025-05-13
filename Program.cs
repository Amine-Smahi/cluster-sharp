using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;
using System.Net;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());
builder.Services.AddSingleton<ClusterSetupService>();

builder.Services.AddHostedService<MachineMonitorBackgroundService>();
builder.Services.AddHostedService<ContainerMonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

builder.Services.AddFastEndpoints(options => options.SourceGeneratorDiscoveredTypes = []);

builder.Services.AddHttpClient("ReverseProxyClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
        KeepAlivePingDelay = TimeSpan.FromSeconds(5),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(60),
        ConnectTimeout = TimeSpan.FromSeconds(30),
        MaxConnectionsPerServer = 100
    })
    .ConfigureHttpClient(client => {
        client.Timeout = TimeSpan.FromMinutes(2);
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.NotFound)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);
app.UseRouting();

app.UseReverseProxy();

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.Run();