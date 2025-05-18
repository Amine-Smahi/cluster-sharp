using System;
using System.IO;
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
            catch (Exception ex) when (IsConnectionIssue(ex))
            {
                _logger.LogInformation("Connection issue handled: {Message}", ex.Message);
                
                // If the response hasn't started yet, we can set the status code to 200 OK
                if (!context.Response.HasStarted)
                {
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

        private bool IsConnectionIssue(Exception ex)
        {
            // Handle connection resets
            if (ex is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionReset)
                return true;
                
            // Handle broken pipe or connection reset by peer
            if (ex is IOException ioEx && (
                ioEx.Message.Contains("Broken pipe") || 
                ioEx.Message.Contains("Connection reset by peer")))
                return true;
                
            // Handle operation cancelled exceptions (client cancellations)
            if (ex is OperationCanceledException || 
                ex is TaskCanceledException || 
                (ex.InnerException != null && (
                    ex.InnerException is OperationCanceledException || 
                    ex.InnerException is TaskCanceledException)))
                return true;
                
            return false;
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