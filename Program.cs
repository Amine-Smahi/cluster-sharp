using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;
using System.Net;
using System.Net.Sockets;
using System.IO;
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
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
        KeepAlivePingDelay = TimeSpan.FromSeconds(15),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        ConnectTimeout = TimeSpan.FromSeconds(20),
        MaxConnectionsPerServer = 1500,
        EnableMultipleHttp2Connections = true,
        MaxResponseDrainSize = 1024 * 1024 * 2,
        ResponseDrainTimeout = TimeSpan.FromSeconds(5),
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
        }
    })
    .ConfigureHttpClient(client => {
        client.Timeout = TimeSpan.FromMinutes(3);
        client.DefaultRequestVersion = HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    })
    .AddPolicyHandler(Policy<HttpResponseMessage>
        .Handle<TaskCanceledException>()
        .Or<OperationCanceledException>()
        .OrInner<TaskCanceledException>()
        .OrInner<OperationCanceledException>()
        .OrResult(r => r.ReasonPhrase?.Contains("cancel", StringComparison.OrdinalIgnoreCase) == true)
        .FallbackAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") }))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.NotFound)
        .OrResult(msg => msg.StatusCode == HttpStatusCode.ServiceUnavailable)
        .OrResult(msg => msg.StatusCode == HttpStatusCode.GatewayTimeout)
        .Or<TimeoutException>()
        .Or<SocketException>()
        .Or<IOException>()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(1.5, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                string errorDetails = outcome.Exception?.Message ?? 
                                   (outcome.Result != null ? $"Status code: {(int)outcome.Result.StatusCode} ({outcome.Result.StatusCode})" : "Unknown error");
                
                Console.WriteLine($"Request failed: {errorDetails}. Retry attempt {retryAttempt}. Waiting {timespan.TotalSeconds} seconds");
            }))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => (int)msg.StatusCode >= 500)
        .Or<TimeoutException>()
        .Or<SocketException>()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, breakDelay) =>
            {
                string reason = outcome.Exception?.Message ?? $"Status code: {(int?)outcome.Result?.StatusCode}";
                Console.WriteLine($"Circuit breaker opened for {breakDelay.TotalSeconds} seconds due to: {reason}");
            },
            onReset: () => Console.WriteLine("Circuit breaker reset - normal operation resumed"),
            onHalfOpen: () => Console.WriteLine("Circuit breaker half-open - testing if service is healthy")
        ));

// Configure Kestrel for high loads
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxConcurrentConnections = 100000;
    serverOptions.Limits.MaxConcurrentUpgradedConnections = 100000;
    serverOptions.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30MB
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
    serverOptions.Limits.MinRequestBodyDataRate = null; // Disable data rate limiting
    serverOptions.AllowSynchronousIO = false;
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