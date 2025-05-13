using System.Collections.Concurrent;

namespace ClusterSharp.Api.Services
{
    public class RoundRobinLoadBalancerService : ILoadBalancerService
    {
        private readonly ConcurrentDictionary<string, int> _currentIndexes = new();
        private readonly ICircuitBreakerService _circuitBreakerService;
        private readonly ILogger<RoundRobinLoadBalancerService> _logger;

        public RoundRobinLoadBalancerService(
            ICircuitBreakerService circuitBreakerService,
            ILogger<RoundRobinLoadBalancerService> logger)
        {
            _circuitBreakerService = circuitBreakerService;
            _logger = logger;
        }

        public string GetNextEndpoint(string sourceHost, List<string> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0)
                return string.Empty;

            // If there's only one endpoint, check if it's available
            if (endpoints.Count == 1)
            {
                var endpoint = endpoints[0];
                if (!_circuitBreakerService.IsCircuitOpen(endpoint)) 
                    return endpoint;
                
                _logger.LogWarning("Circuit is open for the only available endpoint {Endpoint}", endpoint);
                return string.Empty;
            }
            
            // Filter out endpoints with open circuits
            var availableEndpoints = endpoints.Where(e => !_circuitBreakerService.IsCircuitOpen(e)).ToList();
            
            if (availableEndpoints.Count == 0)
            {
                _logger.LogWarning("All endpoints for {Host} are in open circuit state", sourceHost);
                
                // Try to find a half-open endpoint to test
                var halfOpenEndpoints = endpoints.Where(_circuitBreakerService.IsCircuitHalfOpen).ToList();
                if (halfOpenEndpoints.Count <= 0) 
                    return string.Empty;
                
                var endpoint = halfOpenEndpoints[0];
                _logger.LogInformation("Trying half-open circuit endpoint {Endpoint}", endpoint);
                return endpoint;
            }

            // Standard round-robin selection
            var currentIndex = _currentIndexes.GetOrAdd(sourceHost, _ => 0);
            var startIndex = currentIndex;
            string targetHost;
            
            do {
                var nextIndex = (currentIndex + 1) % endpoints.Count;
                targetHost = endpoints[currentIndex];
                
                _currentIndexes.TryUpdate(sourceHost, nextIndex, currentIndex);
                currentIndex = nextIndex;
                
                if (!_circuitBreakerService.IsCircuitOpen(targetHost))
                    break;
                
            } while (currentIndex != startIndex);
            
            return targetHost;
        }
    }
} 