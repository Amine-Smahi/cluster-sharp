using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.BackgroundServices;

public class ContainerMonitorBackgroundService(ClusterOverviewService clusterOverviewService) : BackgroundService
{
    private readonly TimeSpan _monitorInterval = TimeSpan.FromMilliseconds(100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_monitorInterval, stoppingToken);
            
            try
            {
                var cluster = ClusterHelper.GetClusterSetup();
                if (cluster == null)
                {
                    Console.WriteLine("Error retrieving cluster setup");
                    continue;
                }
                
                var nodeProcessingTasks = cluster.Nodes
                    .Select(x => x.Hostname)
                    .Select(ProcessNodeAsync)
                    .ToList();
                
                var processedNodes = await Task.WhenAll(nodeProcessingTasks);
                
                var clusterInfo = processedNodes.Where(node => node != null).ToList();
                
                FileHelper.SetContentToFile("Assets/container-info.json", clusterInfo, out var errorMessage);
                if (errorMessage != null)
                {
                    Console.WriteLine($"Error writing container info to file: {errorMessage}");
                    continue;
                }

                clusterOverviewService.UpdateContainerInfo();
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
            // Skip processing if machine is down
            if (MachineMonitorBackgroundService.MachineStatus.TryGetValue(worker, out bool isUp) && !isUp)
            {
                return new Node
                {
                    Hostname = worker,
                    MachineStats = new MachineStats(),
                    Containers = [] // Return empty container list for down machines
                };
            }
            
            var containers = SshHelper.GetDockerContainerStats(worker);
            if (containers == null)
            {
                Console.WriteLine($"Error retrieving container stats for {worker}");
                return null;
            }

            return new Node
            {
                Hostname = worker,
                MachineStats = new MachineStats(), // Empty machine stats since we're only handling containers
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