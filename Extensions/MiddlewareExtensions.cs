namespace ClusterSharp.Api.Extensions
{
    public static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseYarpExceptionHandling(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                try
                {
                    await next.Invoke();
                }
                catch (Exception ex) when (ex.Message.Contains("No destination available for") ||
                                          ex.Message.Contains("Failed to resolve destination") ||
                                          ex is TaskCanceledException ||
                                          ex is OperationCanceledException ||
                                          (ex is System.Threading.Tasks.TaskCanceledException && ex.Message.Contains("The request was canceled")))
                {
                    // For request cancellations, respond with appropriate status
                    if (ex is TaskCanceledException || ex is OperationCanceledException || 
                        (ex.Message.Contains("The request was canceled") || ex.Message.Contains("RequestCanceled")))
                    {
                        context.Response.StatusCode = 499; // Client Closed Request
                        return;
                    }
                    
                    await Task.Delay(100);
                    try
                    {
                        if (context.Request.Body.CanSeek)
                        {
                            context.Request.Body.Position = 0;
                        }

                        await next.Invoke();
                    }
                    catch
                    {
                        context.Response.StatusCode = 503;
                        await context.Response.WriteAsync("Service temporarily unavailable. Please try again.");
                    }
                }
            });
        }

        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                context.Response.Headers.Remove("Server");
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";

                var path = context.Request.Path.ToString().ToLowerInvariant();
                if (path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".jpg") ||
                    path.EndsWith(".png") || path.EndsWith(".gif") || path.EndsWith(".woff2"))
                {
                    context.Response.Headers["Cache-Control"] = "public,max-age=86400";
                }

                await next.Invoke();
            });
        }
    }
} 