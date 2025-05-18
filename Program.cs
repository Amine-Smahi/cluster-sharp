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
        client.Timeout = TimeSpan.FromMinutes(5);
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.NotFound)
        .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
        .OrResult(msg => msg.StatusCode == HttpStatusCode.GatewayTimeout)
        .Or<OperationCanceledException>()
        .Or<TimeoutException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(1.5, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                string errorDetails = outcome.Exception?.Message ?? 
                                      (outcome.Result != null ? $"Status code: {(int)outcome.Result.StatusCode} ({outcome.Result.StatusCode})" : "Unknown error");
                
                Console.WriteLine($"Request failed: {errorDetails}. Retry attempt {retryAttempt}. Waiting {timespan.TotalSeconds} seconds");
            }));

// Configure Kestrel for high loads
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxConcurrentConnections = 100000;
    serverOptions.Limits.MaxConcurrentUpgradedConnections = 100000;
    serverOptions.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30MB
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
});

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);
app.UseRouting();

app.UseReverseProxy();

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.Run("http://0.0.0.0:80");