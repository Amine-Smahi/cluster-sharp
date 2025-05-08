using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.Middleware;

public class RequestTrackerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestStatsService _requestStatsService;

    public RequestTrackerMiddleware(RequestDelegate next, RequestStatsService requestStatsService)
    {
        _next = next;
        _requestStatsService = requestStatsService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _requestStatsService.RecordRequest();
        await _next(context);
    }
}


public static class RequestTrackerMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestTracker(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestTrackerMiddleware>();
    }
} 