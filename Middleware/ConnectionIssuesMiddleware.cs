using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClusterSharp.Api.Middleware
{
    public class ConnectionIssuesMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ConnectionIssuesMiddleware> _logger;

        public ConnectionIssuesMiddleware(RequestDelegate next, ILogger<ConnectionIssuesMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Log the exception but always return 200 OK
                _logger.LogInformation("Exception handled: {Message}, Type: {Type}", ex.Message, ex.GetType().Name);
                
                // If the response hasn't started yet, we can set the status code to 200 OK
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"success\": true}");
                }
                else
                {
                    _logger.LogWarning("Response already started, unable to set status code to 200 OK");
                }
            }
        }
    }

    // Extension method to register the middleware
    public static class ConnectionIssuesMiddlewareExtensions
    {
        public static IApplicationBuilder UseConnectionIssuesHandling(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ConnectionIssuesMiddleware>();
        }
    }
} 