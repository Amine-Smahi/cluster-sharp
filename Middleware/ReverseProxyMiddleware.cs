using System.Net.Http.Headers;
using System.Collections.Concurrent;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Services;
using Microsoft.Extensions.Primitives;

namespace ClusterSharp.Api.Middleware
{
    public class ReverseProxyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ReverseProxyMiddleware> _logger;
        private readonly ProxyRule _proxyRule;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ICircuitBreakerService _circuitBreakerService;
        private readonly ILoadBalancerService _loadBalancerService;
        private readonly TimeSpan _requestTimeout;

        public ReverseProxyMiddleware(
            RequestDelegate next,
            ILogger<ReverseProxyMiddleware> logger,
            ProxyRule proxyRule,
            IHttpClientFactory httpClientFactory,
            ICircuitBreakerService circuitBreakerService,
            ILoadBalancerService loadBalancerService,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _proxyRule = proxyRule;
            _httpClientFactory = httpClientFactory;
            _circuitBreakerService = circuitBreakerService;
            _loadBalancerService = loadBalancerService;
            _requestTimeout = TimeSpan.FromSeconds(
                configuration.GetValue<int>("Proxy:RequestTimeoutSeconds", 30));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var host = context.Request.Host.Host.ToLower();
            var targetHost = DetermineTargetHost(host);

            if (string.IsNullOrEmpty(targetHost))
            {
                await _next(context);
                return;
            }

            try
            {
                await ProxyRequest(context, targetHost);
                await _circuitBreakerService.ResetCircuitBreakerOnSuccessAsync(targetHost);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Request to {TargetHost} timed out after {Timeout} seconds", targetHost, _requestTimeout.TotalSeconds);
                await _circuitBreakerService.RecordFailureAsync(targetHost);
                context.Response.StatusCode = 504;
                await context.Response.WriteAsync("Request to upstream server timed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in proxy middleware for host {Host} to target {Target}", host, targetHost);
                await _circuitBreakerService.RecordFailureAsync(targetHost);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error in proxy");
            }
        }

        private string DetermineTargetHost(string host)
        {
            if (!_proxyRule.Rules.TryGetValue(host, out var endpoints) || endpoints.Count == 0)
                return string.Empty;

            return _loadBalancerService.GetNextEndpoint(host, endpoints);
        }

        private async Task ProxyRequest(HttpContext context, string targetUri)
        {
            var httpClient = _httpClientFactory.CreateClient("ReverseProxy");
            httpClient.Timeout = _requestTimeout;
            
            var targetUrl = $"http://{targetUri}{context.Request.Path}{context.Request.QueryString}";
            var requestMessage = new HttpRequestMessage();
            requestMessage.Method = new HttpMethod(context.Request.Method);
            requestMessage.RequestUri = new Uri(targetUrl);
            foreach (var header in context.Request.Headers)
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) &&
                    requestMessage.Content != null)
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());

            requestMessage.Headers.Host = targetUri.Split(':')[0];
            if (context.Request.ContentLength > 0)
            {
                var streamContent = new StreamContent(context.Request.Body);
                if (context.Request.Headers.ContentType.Count > 0)
                    streamContent.Headers.ContentType =
                        MediaTypeHeaderValue.Parse(context.Request.Headers.ContentType.ToString());
                requestMessage.Content = streamContent;
            }

            using var cts = new CancellationTokenSource(_requestTimeout);
            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, context.RequestAborted);
                
            using var responseMessage = await httpClient.SendAsync(requestMessage,
                HttpCompletionOption.ResponseHeadersRead, combinedCts.Token);
                    
            context.Response.StatusCode = (int)responseMessage.StatusCode;

            foreach (var header in responseMessage.Headers)
                context.Response.Headers[header.Key] = new StringValues(header.Value.ToArray());

            foreach (var header in responseMessage.Content.Headers)
                if (!context.Response.Headers.ContainsKey(header.Key))
                    context.Response.Headers[header.Key] = new StringValues(header.Value.ToArray());

            await responseMessage.Content.CopyToAsync(context.Response.Body, combinedCts.Token);
        }
    }

    public static class ReverseProxyMiddlewareExtensions
    {
        public static void UseReverseProxy(this IApplicationBuilder builder)
        {
            builder.UseMiddleware<ReverseProxyMiddleware>();
        }
    }
}