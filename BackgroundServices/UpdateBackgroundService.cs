using ClusterSharp.Api.Helpers;

namespace ClusterSharp.Api.BackgroundServices
{
    public class UpdateBackgroundService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var member in ClusterHelper.GetClusterSetup()?.Members!)
                        SshHelper.ExecuteCommands(member.Hostname, CommandHelper.GetClusterUpdateCommands());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing update: {ex.Message}");
                }
                
                var delay = CalculateDelay();
                await Task.Delay(delay, stoppingToken);
            }
        }

        private static TimeSpan CalculateDelay()
        {
            var now = DateTime.Now;
            var nextRun = now.Date.AddDays(now.Hour >= 2 ? 1 : 0).AddHours(2);
            var delay = nextRun - now;
            return delay;
        }
    }
} 