using System.Net.Http.Headers;
using System.Collections.Concurrent;
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
        
        private readonly ConcurrentDictionary<string, int> _currentIndexes = new();
        private readonly ConcurrentDictionary<string, EndpointCircuitState> _circuitBreakers = new();
        
        private readonly int _failureThreshold = 3;
        private readonly TimeSpan _breakerResetTimeout = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);

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
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Request to {TargetHost} timed out after {Timeout} seconds", targetHost, _requestTimeout.TotalSeconds);
                await RecordFailureAsync(targetHost);
                context.Response.StatusCode = 504;
                await context.Response.WriteAsync("Request to upstream server timed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in proxy middleware for host {Host} to target {Target}", host, targetHost);
                await RecordFailureAsync(targetHost);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error in proxy");
            }
        }

        private Task<string> DetermineTargetHostAsync(string host)
        {
            if (!_proxyRule.Rules.TryGetValue(host, out var endpoints) || endpoints.Count == 0)
                return Task.FromResult(string.Empty);

            if (endpoints.Count == 1)
            {
                var endpoint = endpoints[0];
                if (!IsCircuitOpen(endpoint)) 
                    return Task.FromResult(endpoint);
                
                _logger.LogWarning("Circuit is open for the only available endpoint {Endpoint}", endpoint);
                return Task.FromResult(string.Empty);
            }
            
            var availableEndpoints = endpoints.Where(e => !IsCircuitOpen(e)).ToList();
            
            if (availableEndpoints.Count == 0)
            {
                _logger.LogWarning("All endpoints for {Host} are in open circuit state", host);
                
                var halfOpenEndpoints = endpoints.Where(IsCircuitHalfOpen).ToList();
                if (halfOpenEndpoints.Count <= 0) 
                    return Task.FromResult(string.Empty);
                
                var endpoint = halfOpenEndpoints[0];
                _logger.LogInformation("Trying half-open circuit endpoint {Endpoint}", endpoint);
                return Task.FromResult(endpoint);
            }
            
            var currentIndex = _currentIndexes.GetOrAdd(host, _ => 0);
            
            var startIndex = currentIndex;
            string targetHost;
            
            do {
                var nextIndex = (currentIndex + 1) % endpoints.Count;
                targetHost = endpoints[currentIndex];
                
                _currentIndexes.TryUpdate(host, nextIndex, currentIndex);
                currentIndex = nextIndex;
                
                if (!IsCircuitOpen(targetHost))
                    break;
                
            } while (currentIndex != startIndex);
            
            return Task.FromResult(targetHost);
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
        
        private bool IsCircuitOpen(string endpoint)
        {
            if (!_circuitBreakers.TryGetValue(endpoint, out var state))
                return false;
  
            if (state.State != CircuitState.Open || DateTime.UtcNow - state.LastFailureTime <= _breakerResetTimeout)
                return state.State == CircuitState.Open;
            
            var updatedState = new EndpointCircuitState 
            {
                State = CircuitState.HalfOpen,
                FailureCount = state.FailureCount,
                LastFailureTime = state.LastFailureTime
            };
            
            _circuitBreakers.TryUpdate(endpoint, updatedState, state);
            _logger.LogInformation("Circuit for {Endpoint} changed from Open to Half-Open", endpoint);
            return false;
        }
        
        private bool IsCircuitHalfOpen(string endpoint)
        {
            if (!_circuitBreakers.TryGetValue(endpoint, out var state))
                return false;
                
            return state.State == CircuitState.HalfOpen;
        }
        
        private Task RecordFailureAsync(string endpoint)
        {
            _circuitBreakers.AddOrUpdate(
                endpoint,
                _ => new EndpointCircuitState 
                { 
                    FailureCount = 1, 
                    LastFailureTime = DateTime.UtcNow,
                    State = CircuitState.Closed
                },
                (_, existingState) => 
                {
                    var newState = new EndpointCircuitState
                    {
                        FailureCount = existingState.FailureCount + 1,
                        LastFailureTime = DateTime.UtcNow,
                        State = existingState.State
                    };
                    if (newState.FailureCount < _failureThreshold || newState.State == CircuitState.Open)
                        return newState;
                    
                    newState.State = CircuitState.Open;
                    _logger.LogWarning("Circuit breaker opened for endpoint {Endpoint} after {Count} failures", 
                        endpoint, newState.FailureCount);

                    return newState;
                }
            );
            
            return Task.CompletedTask;
        }
        
        private Task ResetCircuitBreakerOnSuccessAsync(string endpoint)
        {
            if (!_circuitBreakers.TryGetValue(endpoint, out var state) || state.State != CircuitState.HalfOpen)
                return Task.CompletedTask;
            
            _circuitBreakers.TryUpdate(
                endpoint,
                new EndpointCircuitState 
                { 
                    State = CircuitState.Closed,
                    FailureCount = 0,
                    LastFailureTime = state.LastFailureTime 
                },
                state
            );
            
            _logger.LogInformation("Circuit breaker closed for endpoint {Endpoint} after successful request", endpoint);
            return Task.CompletedTask;
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
        public int FailureCount { get; init; }
        public DateTime LastFailureTime { get; init; } = DateTime.MinValue;
    }

    public static class ReverseProxyMiddlewareExtensions
    {
        public static void UseReverseProxy(this IApplicationBuilder builder)
        {
            builder.UseMiddleware<ReverseProxyMiddleware>();
        }
    }
}