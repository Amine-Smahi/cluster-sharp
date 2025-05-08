using System.Collections.Concurrent;
using ClusterSharp.Api.Models.Stats;

namespace ClusterSharp.Api.Services;

public class RequestStatsService
{
    private readonly ConcurrentQueue<DateTime> _requestTimes = new();
    private readonly TimeSpan _timeWindow = TimeSpan.FromSeconds(10);
    
    public RequestStats GetCurrentStats()
    {
        
        var cutoffTime = DateTime.UtcNow - _timeWindow;
        while (_requestTimes.TryPeek(out var oldestTime) && oldestTime < cutoffTime)
        {
            _requestTimes.TryDequeue(out _);
        }
        
        var requestCount = _requestTimes.Count;
        var requestsPerSecond = requestCount / _timeWindow.TotalSeconds;
        
        return new RequestStats
        {
            Timestamp = DateTime.UtcNow,
            RequestsPerSecond = Math.Round(requestsPerSecond, 2)
        };
    }
    
    public void RecordRequest()
    {
        _requestTimes.Enqueue(DateTime.UtcNow);
    }
} 