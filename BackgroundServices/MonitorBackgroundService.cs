using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.BackgroundServices;

public class MonitorBackgroundService(ClusterOverviewService clusterOverviewService) : BackgroundService
{
    private readonly TimeSpan _monitorInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_monitorInterval, stoppingToken);
            
            try
            {
                var clusterInfo = new List<Node>();
                var cluster = ClusterHelper.GetClusterSetup();
                if (cluster == null)
                {
                    Console.WriteLine("Error retrieving cluster setup");
                    continue;
                }
                
                foreach (var worker in cluster.Nodes.Select(x => x.Hostname))
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

                    clusterInfo.Add(new Node
                    {
                        Hostname = worker,
                        MachineStats = new MachineStats
                        {
                            Cpu = machineStats.Cpu,
                            Memory = machineStats.Memory,
                            Disk = machineStats.Disk
                        },
                        Containers = containers.Select(c => new ContainerStats
                        {
                            Name = c.Name,
                            Cpu = c.Cpu,
                            Memory = new Stat { Value = c.Memory.Value, Percentage = c.Memory.Percentage },
                            Disk = new Stat { Value = c.Disk.Value, Percentage = c.Disk.Percentage },
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

                ClusterHelper.GenerateClusterOverview();
                clusterOverviewService.UpdateOverview();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error monitoring the cluster: {e.Message}");
            }
        }
    }
}