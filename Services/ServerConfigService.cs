using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace ClusterSharp.Api.Services
{
    public class ServerConfigService : IHostedService
    {
        private readonly ILogger<ServerConfigService> _logger;

        public ServerConfigService(ILogger<ServerConfigService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ConfigureServicePointManager();
            _logger.LogInformation("Advanced server configuration applied");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void ConfigureServicePointManager()
        {
            // Set aggressive ServicePointManager configurations to handle connection issues
            ServicePointManager.DefaultConnectionLimit = 10000;
            ServicePointManager.MaxServicePointIdleTime = (int)TimeSpan.FromMinutes(20).TotalMilliseconds;
            ServicePointManager.ReusePort = true;
            ServicePointManager.DnsRefreshTimeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
            ServicePointManager.EnableDnsRoundRobin = true;
            
            // Set TCP options for better handling of connection resets
            ServicePointManager.UseNagleAlgorithm = false;  // Disable Nagle's algorithm for better performance

            // Set longer timeouts for client connections
            ServicePointManager.FindServicePoint(new Uri("http://localhost")).ConnectionLeaseTimeout = (int)TimeSpan.FromMinutes(30).TotalMilliseconds;
            
            _logger.LogInformation("ServicePointManager configured for high throughput and resilience");
        }
    }
} 