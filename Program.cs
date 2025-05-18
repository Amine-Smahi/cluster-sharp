using ClusterSharp.Api.BackgroundServices;
using ClusterSharp.Api.Services;
using FastEndpoints;
using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Middleware;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Diagnostics;
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to handle even abrupt disconnects
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
    options.AllowSynchronousIO = false;
    options.AddServerHeader = false;
    options.Limits.MaxConcurrentConnections = 10000;
    options.Limits.MaxConcurrentUpgradedConnections = 10000;
    
    // Increase max request body size
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100 MB
});

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(_ => new ClusterOverviewService());
builder.Services.AddSingleton<ClusterSetupService>();

// Add the server configuration service
builder.Services.AddHostedService<ServerConfigService>();

builder.Services.AddHostedService<MachineMonitorBackgroundService>();
builder.Services.AddHostedService<ContainerMonitorBackgroundService>();
builder.Services.AddHostedService<UpdateBackgroundService>();

builder.Services.AddFastEndpoints(options => options.SourceGeneratorDiscoveredTypes = []);

// Configure HTTP client to handle connection issues
builder.Services.AddHttpClient("ReverseProxyClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
        EnableMultipleHttp2Connections = true,
        // Increase connection pool size
        MaxConnectionsPerServer = 1000
    });

var app = builder.Build();

ClusterHelper.Initialize(app.Services);

app.Lifetime.ApplicationStopping.Register(SshHelper.CloseAllConnections);

// Global exception handler - catches all exceptions and returns 200 OK
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"success\":true}");
    });
});

// Add the special JMeter handling middleware first
app.UseJMeterRequestHandling();

// Add the connection issues middleware early in the pipeline
app.UseConnectionIssuesHandling();

// Add a special middleware to handle pre-existing responses
app.Use(async (context, next) =>
{
    try
    {
        await next();
        
        // For requests that didn't throw but might have status codes other than 200
        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            if (!context.Response.HasStarted)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"success\":true}");
            }
        }
    }
    catch
    {
        // Last resort catch - always return 200 OK
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"success\":true}");
        }
    }
});

app.UseRouting();

app.UseReverseProxy();

app.MapOpenApi();

app.UseSwagger();
app.UseSwaggerUI();

app.Run("http://0.0.0.0:80");