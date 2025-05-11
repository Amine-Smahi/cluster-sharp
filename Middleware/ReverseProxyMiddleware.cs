using System.Net.Http.Headers;
using ClusterSharp.Api.Models.Cluster;
using Microsoft.Extensions.Primitives;

namespace ClusterSharp.Api.Middleware
{
    public class ReverseProxyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ReverseProxyMiddleware> _logger;
        private readonly ProxyRule _proxyRule;
        private readonly HttpClient _httpClient;

        public ReverseProxyMiddleware(
            RequestDelegate next,
            ILogger<ReverseProxyMiddleware> logger,
            ProxyRule proxyRule)
        {
            _next = next;
            _logger = logger;
            _proxyRule = proxyRule;

            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                MaxConnectionsPerServer = 1000,
                EnableMultipleHttp2Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(5)
            };

            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(100);
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
                try 
                {
                    await ProxyRequest(context, targetHost);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while proxying request to {TargetHost}", targetHost);
                    context.Response.StatusCode = 502; 
                    await context.Response.WriteAsync("Failed to proxy request");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in proxy middleware for host {Host}", host);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error in proxy");
            }
        }

        private string DetermineTargetHost(string host)
        {
            if (_proxyRule.Rules.TryGetValue(host, out var endpoints) && endpoints.Count > 0)
                return endpoints[0];
            return string.Empty;
        }

        private async Task ProxyRequest(HttpContext context, string targetUri)
        {
            var targetUrl = $"http://{targetUri}{context.Request.Path}{context.Request.QueryString}";
            var requestMessage = new HttpRequestMessage();
            requestMessage.Method = new HttpMethod(context.Request.Method);
            requestMessage.RequestUri = new Uri(targetUrl);
            foreach (var header in context.Request.Headers)
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) &&
                    requestMessage.Content != null) requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            
            requestMessage.Headers.Host = targetUri.Split(':')[0];
            if (context.Request.ContentLength > 0)
            {
                var streamContent = new StreamContent(context.Request.Body);
                if (context.Request.Headers.ContentType.Count > 0) 
                    streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.Headers.ContentType.ToString());
                requestMessage.Content = streamContent;
            }
            using var responseMessage = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
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