using ClusterSharp.Api.Helpers;

namespace ClusterSharp.Api.BackgroundServices
{
    public class UpdateBackgroundService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = CalculateDelay();
                await Task.Delay(delay, stoppingToken);
                
                try
                {
                    Console.WriteLine(nameof(UpdateBackgroundService));
                    foreach (var member in ClusterHelper.GetClusterSetup()?.Nodes!)
                    {
                        SshHelper.ExecuteCommands(member.Hostname, CommandHelper.GetUpdateMachineCommands());
                        Console.WriteLine($"Machine {member.Hostname} updated successfully.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing update: {ex.Message}");
                }
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