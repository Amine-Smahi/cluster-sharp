using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;

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
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
        KeepAlivePingDelay = TimeSpan.FromSeconds(1),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
    });

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);
app.UseRouting();

app.UseFastEndpoints(config => config.Serializer.Options.PropertyNamingPolicy = null);

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.Run();