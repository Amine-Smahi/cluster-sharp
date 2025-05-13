using System.Collections.Concurrent;

namespace ClusterSharp.Api.Services
{
    public interface ICircuitBreakerService
    {
        bool IsCircuitOpen(string endpoint);
        bool IsCircuitHalfOpen(string endpoint);
        Task RecordFailureAsync(string endpoint);
        Task ResetCircuitBreakerOnSuccessAsync(string endpoint);
    }
    
    public class CircuitBreakerService : ICircuitBreakerService
    {
        private readonly ILogger<CircuitBreakerService> _logger;
        private readonly ConcurrentDictionary<string, EndpointCircuitState> _circuitBreakers = new();
        
        private readonly int _failureThreshold;
        private readonly TimeSpan _breakerResetTimeout;
        
        public CircuitBreakerService(ILogger<CircuitBreakerService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _failureThreshold = configuration.GetValue<int>("CircuitBreaker:FailureThreshold", 3);
            _breakerResetTimeout = TimeSpan.FromSeconds(
                configuration.GetValue<int>("CircuitBreaker:ResetTimeoutSeconds", 60));
        }
        
        public bool IsCircuitOpen(string endpoint)
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
        
        public bool IsCircuitHalfOpen(string endpoint)
        {
            if (!_circuitBreakers.TryGetValue(endpoint, out var state))
                return false;
                
            return state.State == CircuitState.HalfOpen;
        }
        
        public Task RecordFailureAsync(string endpoint)
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
        
        public Task ResetCircuitBreakerOnSuccessAsync(string endpoint)
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
} 