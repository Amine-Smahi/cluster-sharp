using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Services;
using ZLinq;

namespace ClusterSharp.Api.BackgroundServices;

public class ContainerMonitorBackgroundService(
    ClusterOverviewService clusterOverviewService)
    : BackgroundService
{
    private readonly TimeSpan _successInterval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _errorInterval = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _semaphore.WaitAsync(stoppingToken);
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
                        .AsValueEnumerable()
                        .Select(x => x.Hostname)
                        .Select(ProcessNodeAsync)
                        .ToList();

                    var processedNodes = await Task.WhenAll(nodeProcessingTasks);

                    var clusterInfo = processedNodes.AsValueEnumerable().Where(node => node != null).ToList();

                    if (clusterInfo.Count == 0)
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
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error monitoring the containers: {e.Message}");
            }
        }
    }

    private static async Task<Node?> ProcessNodeAsync(string worker)
    {
        return await Task.Run(() => {
            var containers = SshHelper.GetDockerContainerStats(worker);
            if (containers == null || containers.Count == 0)
                return null;

            return new Node
            {
                Hostname = worker,
                MachineStats = new MachineStats(), 
                Containers = containers.AsValueEnumerable().Select(c => new ContainerStats
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