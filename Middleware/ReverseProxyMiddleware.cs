using System.Net.Http.Headers;
using System.Threading;
using ClusterSharp.Api.Models.Cluster;
using Microsoft.Extensions.Primitives;

namespace ClusterSharp.Api.Middleware
{
    public class ReverseProxyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ReverseProxyMiddleware> _logger;
        private readonly ProxyRule _proxyRule;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Dictionary<string, int> _currentIndexes = new();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public ReverseProxyMiddleware(
            RequestDelegate next,
            ILogger<ReverseProxyMiddleware> logger,
            ProxyRule proxyRule,
            IHttpClientFactory httpClientFactory)
        {
            _next = next;
            _logger = logger;
            _proxyRule = proxyRule;
            _httpClientFactory = httpClientFactory;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var host = context.Request.Host.Host.ToLower();
            var targetHost = await DetermineTargetHostAsync(host);

            if (string.IsNullOrEmpty(targetHost))
            {
                await _next(context);
                return;
            }

            try
            {
                await ProxyRequest(context, targetHost);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in proxy middleware for host {Host}", host);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error in proxy");
            }
        }

        private async Task<string> DetermineTargetHostAsync(string host)
        {
            if (!_proxyRule.Rules.TryGetValue(host, out var endpoints) || endpoints.Count == 0)
                return string.Empty;

            if (endpoints.Count == 1)
                return endpoints[0];
            
            await _lock.WaitAsync();
            try
            {
                if (!_currentIndexes.TryGetValue(host, out var currentIndex))
                {
                    currentIndex = 0;
                    _currentIndexes[host] = 0;
                }
                var targetHost = endpoints[currentIndex];
                _currentIndexes[host] = (currentIndex + 1) % endpoints.Count;
                
                return targetHost;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task ProxyRequest(HttpContext context, string targetUri)
        {
            var httpClient = _httpClientFactory.CreateClient("ReverseProxy");
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

            using var responseMessage = await httpClient.SendAsync(requestMessage,
                HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)responseMessage.StatusCode;

            foreach (var header in responseMessage.Headers)
                context.Response.Headers[header.Key] = new StringValues(header.Value.ToArray());

            foreach (var header in responseMessage.Content.Headers)
                if (!context.Response.Headers.ContainsKey(header.Key))
                    context.Response.Headers[header.Key] = new StringValues(header.Value.ToArray());

            await responseMessage.Content.CopyToAsync(context.Response.Body);
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