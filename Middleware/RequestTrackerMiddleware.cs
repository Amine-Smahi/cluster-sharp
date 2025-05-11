using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.Middleware;

public class RequestTrackerMiddleware(RequestDelegate next, RequestStatsService requestStatsService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        requestStatsService.RecordRequest();
        await next(context);
    }
}

public static class RequestTrackerMiddlewareExtensions
{
    public static void UseRequestTracker(this IApplicationBuilder builder)
    {
        builder.UseMiddleware<RequestTrackerMiddleware>();
    }
} 