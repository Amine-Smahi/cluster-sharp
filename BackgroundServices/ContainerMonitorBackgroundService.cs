using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.BackgroundServices;

public class ContainerMonitorBackgroundService(ClusterOverviewService clusterOverviewService) : BackgroundService
{
    private readonly TimeSpan _successInterval = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _errorInterval = TimeSpan.FromMilliseconds(5000);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cluster = ClusterHelper.GetClusterSetup();
                if (cluster == null)
                {
                    Console.WriteLine("Error retrieving cluster setup");
                    await Task.Delay(_errorInterval, stoppingToken);
                    continue;
                }
                
                var nodeProcessingTasks = cluster.Nodes
                    .Select(x => x.Hostname)
                    .Select(ProcessNodeAsync)
                    .ToList();
                
                var processedNodes = await Task.WhenAll(nodeProcessingTasks);
                
                var clusterInfo = processedNodes.Where(node => node != null).ToList();
                
                if(clusterInfo.Count == 0)
                {
                    Console.WriteLine("No container stats retrieved.");
                    await Task.Delay(_errorInterval, stoppingToken);
                    continue;
                }

                FileHelper.SetContentToFile("Assets/container-info.json", clusterInfo, out var errorMessage);
                if (errorMessage != null)
                {
                    Console.WriteLine($"Error writing container info to file: {errorMessage}");
                    continue;
                }

                clusterOverviewService.UpdateContainerInfo();
                await Task.Delay(_successInterval, stoppingToken);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error monitoring the containers: {e.Message}");
            }
        }
    }
    
    private async Task<Node?> ProcessNodeAsync(string worker)
    {
        return await Task.Run(() => {
            var containers = SshHelper.GetDockerContainerStats(worker);
            if (containers == null)
            {
                Console.WriteLine($"Error retrieving container stats for {worker}");
                return null;
            }
            
            if(containers.Count == 0)
            {
                Console.WriteLine($"No containers found on {worker}");
                return null;
            }

            return new Node
            {
                Hostname = worker,
                MachineStats = new MachineStats(), 
                Containers = containers.Select(c => new ContainerStats
                {
                    Name = c.Name,
                    Cpu = c.Cpu,
                    Memory = c.Memory,
                    Disk = c.Disk,
                    ExternalPort = c.ExternalPort
                }).ToList()
            };
        });
    }
} 