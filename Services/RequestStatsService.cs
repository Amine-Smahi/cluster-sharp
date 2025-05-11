using System.Collections.Concurrent;
using ClusterSharp.Api.Models.Stats;

namespace ClusterSharp.Api.Services;

public class RequestStatsService
{
    private readonly ConcurrentQueue<DateTime> _requestTimes = new();
    private readonly TimeSpan _timeWindow = TimeSpan.FromSeconds(10);
    private readonly int _maxQueueSize = 100000; // Limit queue size
    private long _droppedRequests = 0;
    private readonly object _cleanupLock = new();
    private DateTime _lastCleanup = DateTime.UtcNow;
    
    public RequestStats GetCurrentStats()
    {
        CleanupOldRequests();
        
        var requestCount = _requestTimes.Count + _droppedRequests;
        var requestsPerSecond = requestCount / _timeWindow.TotalSeconds;
        
        // Reset dropped requests counter after calculating stats
        Interlocked.Exchange(ref _droppedRequests, 0);
        
        return new RequestStats
        {
            Timestamp = DateTime.UtcNow,
            RequestsPerSecond = Math.Round(requestsPerSecond, 2)
        };
    }
    
    public void RecordRequest()
    {
        // If queue is too large, just increment dropped count instead of adding to queue
        if (_requestTimes.Count >= _maxQueueSize)
        {
            Interlocked.Increment(ref _droppedRequests);
            return;
        }
        
        _requestTimes.Enqueue(DateTime.UtcNow);
        
        // Only clean up periodically to avoid excessive processing
        var now = DateTime.UtcNow;
        if ((now - _lastCleanup).TotalSeconds > 1)
        {
            CleanupOldRequests();
        }
    }
    
    private void CleanupOldRequests()
    {
        // Use lock to prevent multiple simultaneous cleanups
        if (Monitor.TryEnter(_cleanupLock))
        {
            try
            {
                _lastCleanup = DateTime.UtcNow;
                var cutoffTime = _lastCleanup - _timeWindow;
                
                // Dequeue old requests
                while (_requestTimes.TryPeek(out var oldestTime) && oldestTime < cutoffTime)
                {
                    _requestTimes.TryDequeue(out _);
                }
            }
            finally
            {
                Monitor.Exit(_cleanupLock);
            }
        }
    }
} 