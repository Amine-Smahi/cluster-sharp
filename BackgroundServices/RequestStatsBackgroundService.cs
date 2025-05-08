using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.BackgroundServices;

public class RequestStatsBackgroundService(RequestStatsService requestStatsService) : BackgroundService
{
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _ = requestStatsService.GetCurrentStats();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RequestStatsBackgroundService: {ex.Message}");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }
    }
} 