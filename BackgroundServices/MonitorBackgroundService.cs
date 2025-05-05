using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models;
using ClusterSharp.Api.Models.Cluster;

namespace ClusterSharp.Api.BackgroundServices;

public class MonitorBackgroundService(ClusterOverviewService clusterOverviewService) : BackgroundService
{
    private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var clusterInfo = new List<ClusterNode>();
                var workers = ClusterHelper.GetWorkers();
                foreach (var worker in workers)
                {
                    var machineStats = SshHelper.GetMachineStats(worker);
                    if (machineStats == null)
                    {
                        Console.WriteLine($"Error retrieving machine stats for {worker}");
                        continue;
                    }

                    var containers = SshHelper.GetDockerContainerStats(worker);
                    if (containers == null)
                    {
                        Console.WriteLine($"Error retrieving container stats for {worker}");
                        continue;
                    }

                    clusterInfo.Add(new ClusterNode
                    {
                        Hostname = worker,
                        MachineStats = new MachineStats
                        {
                            Cpu = machineStats.Cpu,
                            Memory = new MemStat
                                { Value = machineStats.Memory.Value, Percentage = machineStats.Memory.Percentage },
                            Disk = new DiskStat
                                { Value = machineStats.Disk.Value, Percentage = machineStats.Disk.Percentage }
                        },
                        Containers = containers.Select(c => new ContainerInfo
                        {
                            Name = c.Name,
                            Cpu = c.Cpu,
                            Memory = new MemStat { Value = c.Memory.Value, Percentage = c.Memory.Percentage },
                            Disk = new DiskStat { Value = c.Disk.Value, Percentage = c.Disk.Percentage },
                            ExternalPort = c.ExternalPort
                        }).ToList()
                    });
                }

                FileHelper.SetContentToFile("Assets/cluster-info.json", clusterInfo, out var errorMessage);
                if (errorMessage != null)
                {
                    Console.WriteLine($"Error writing cluster info to file: {errorMessage}");
                    continue;
                }

                ClusterHelper.GenerateClusterOverview("Assets/cluster-info.json");
                clusterOverviewService.UpdateOverview();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error monitoring the cluster: {e.Message}");
            }

            await Task.Delay(_monitorInterval, stoppingToken);
        }
    }
}