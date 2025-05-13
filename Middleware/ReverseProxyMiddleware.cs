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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Dictionary<string, int> _currentIndexes = new();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        
        private readonly int _failureThreshold = 3;
        private readonly TimeSpan _breakerResetTimeout = TimeSpan.FromMinutes(1);
        private readonly Dictionary<string, EndpointCircuitState> _circuitBreakers = new();

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
                await ResetCircuitBreakerOnSuccessAsync(targetHost);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in proxy middleware for host {Host} to target {Target}", host, targetHost);
                await RecordFailureAsync(targetHost);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error in proxy");
            }
        }

        private async Task<string> DetermineTargetHostAsync(string host)
        {
            if (!_proxyRule.Rules.TryGetValue(host, out var endpoints) || endpoints.Count == 0)
                return string.Empty;

            if (endpoints.Count == 1)
            {
                var endpoint = endpoints[0];
                if (IsCircuitOpen(endpoint))
                {
                    _logger.LogWarning("Circuit is open for the only available endpoint {Endpoint}", endpoint);
                    return string.Empty;
                }
                return endpoint;
            }
            
            await _lock.WaitAsync();
            try
            {
                var availableEndpoints = endpoints.Where(e => !IsCircuitOpen(e)).ToList();
                
                if (availableEndpoints.Count == 0)
                {
                    _logger.LogWarning("All endpoints for {Host} are in open circuit state", host);
                    var halfOpenEndpoints = endpoints.Where(e => IsCircuitHalfOpen(e)).ToList();
                    if (halfOpenEndpoints.Count > 0)
                    {
                        var endpoint = halfOpenEndpoints[0]; 
                        _logger.LogInformation("Trying half-open circuit endpoint {Endpoint}", endpoint);
                        return endpoint;
                    }
                    return string.Empty;
                }
                
                if (!_currentIndexes.TryGetValue(host, out var currentIndex))
                {
                    currentIndex = 0;
                    _currentIndexes[host] = 0;
                }
                
                var startIndex = currentIndex;
                string targetHost;
                
                do {
                    var nextIndex = (currentIndex + 1) % endpoints.Count;
                    targetHost = endpoints[currentIndex];
                    currentIndex = nextIndex;
                    
                    if (!IsCircuitOpen(targetHost))
                        break;
                    
                } while (currentIndex != startIndex);
                
                _currentIndexes[host] = currentIndex;
                
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
        
        private bool IsCircuitOpen(string endpoint)
        {
            if (!_circuitBreakers.TryGetValue(endpoint, out var state))
                return false;

            if (state.State != CircuitState.Open || DateTime.UtcNow - state.LastFailureTime <= _breakerResetTimeout)
                return state.State == CircuitState.Open;
            state.State = CircuitState.HalfOpen;
            _circuitBreakers[endpoint] = state;
            _logger.LogInformation("Circuit for {Endpoint} changed from Open to Half-Open", endpoint);
            return false;
        }
        
        private bool IsCircuitHalfOpen(string endpoint)
        {
            if (!_circuitBreakers.TryGetValue(endpoint, out var state))
                return false;
                
            return state.State == CircuitState.HalfOpen;
        }
        
        private async Task RecordFailureAsync(string endpoint)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_circuitBreakers.TryGetValue(endpoint, out var state))
                {
                    state = new EndpointCircuitState();
                }
                
                state.FailureCount++;
                state.LastFailureTime = DateTime.UtcNow;
                
                if (state.FailureCount >= _failureThreshold)
                {
                    state.State = CircuitState.Open;
                    _logger.LogWarning("Circuit breaker opened for endpoint {Endpoint} after {Count} failures", 
                        endpoint, state.FailureCount);
                }
                
                _circuitBreakers[endpoint] = state;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        private async Task ResetCircuitBreakerOnSuccessAsync(string endpoint)
        {
            if (!_circuitBreakers.TryGetValue(endpoint, out var state))
                return;
            
            if (state.State == CircuitState.HalfOpen)
            {
                await _lock.WaitAsync();
                try
                {
                    state.State = CircuitState.Closed;
                    state.FailureCount = 0;
                    _circuitBreakers[endpoint] = state;
                    _logger.LogInformation("Circuit breaker closed for endpoint {Endpoint} after successful request", endpoint);
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
    }
    
    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }
    
    public class EndpointCircuitState
    {
        public CircuitState State { get; set; } = CircuitState.Closed;
        public int FailureCount { get; set; } = 0;
        public DateTime LastFailureTime { get; set; } = DateTime.MinValue;
    }

    public static class ReverseProxyMiddlewareExtensions
    {
        public static void UseReverseProxy(this IApplicationBuilder builder)
        {
            builder.UseMiddleware<ReverseProxyMiddleware>();
        }
    }
}