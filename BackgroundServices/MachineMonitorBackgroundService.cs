using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Services;
using ZLinq;

namespace ClusterSharp.Api.BackgroundServices;

public class MachineMonitorBackgroundService(ClusterOverviewService clusterOverviewService) : BackgroundService
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
                        .Select(x => ProcessNodeAsync(x, cluster.Admin.Username, cluster.Admin.Password))
                        .ToList();
                    
                    var processedNodes = await Task.WhenAll(nodeProcessingTasks);
                    
                    var clusterInfo = processedNodes.AsValueEnumerable().Where(node => node != null).ToList();
                    if(clusterInfo.Count == 0)
                    {
                        Console.WriteLine("No machine stats retrieved.");
                        await Task.Delay(_errorInterval, stoppingToken);
                        continue;
                    }

                    FileHelper.SetContentToFile("Assets/machine-info.json", clusterInfo, out var errorMessage);
                    if (errorMessage != null)
                    {
                        Console.WriteLine($"Error writing machine info to file: {errorMessage}");
                        await Task.Delay(_errorInterval, stoppingToken);
                        continue;
                    }

                    clusterOverviewService.UpdateMachineInfo();
                    await Task.Delay(_successInterval, stoppingToken);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error monitoring the machines: {e.Message}");
            }
        }
    }
    
    private static async Task<Node?> ProcessNodeAsync(string worker, string username, string password)
    {
        return await Task.Run(() => {
            var machineStats = SshHelper.GetMachineStats(worker, username, password);
            if(machineStats == null)
            {
                Console.WriteLine($"Error retrieving machine stats for {worker}");
                return null;
            }
            
            return new Node
            {
                Hostname = worker,
                MachineStats = new MachineStats
                {
                    Cpu = machineStats.Cpu,
                    Memory = machineStats.Memory,
                    Disk = machineStats.Disk
                },
                Containers = []
            };
        });
    }
}