using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Overview;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Shared;

namespace ClusterSharp.Api.Helpers;

public static class ClusterHelper
{
    public static Cluster? GetClusterSetup()
    {
        var result = FileHelper.GetContentFromFile<Cluster>("Assets/cluster-setup.json", out var errorMessage);
        ;
        if (result == null)
            Console.WriteLine(errorMessage);
        return result;
    }

    public static List<string> GetWorkers()
    {
        var result = FileHelper.GetContentFromFile<Cluster>("Assets/cluster-setup.json", out var errorMessage);
        if (result == null)
        {
            Console.WriteLine(errorMessage);
            return [];
        }

        return result.Nodes.Where(m => m.Role == Constants.Worker)
            .Select(x => x.Hostname)
            .ToList();
    }

    public static void GenerateClusterOverview()
    {
        var machineInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/machine-info.json", out var machineErrorMessage);
        var containerInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/container-info.json", out var containerErrorMessage);
        
        if (machineInfo == null)
        {
            Console.WriteLine(machineErrorMessage);
            // Continue with container info only
        }
        
        if (containerInfo == null)
        {
            Console.WriteLine(containerErrorMessage);
            // Continue with machine info only
        }
        
        var overview = new ClusterOverview();
        
        // Process machines
        if (machineInfo != null)
        {
            overview.Machines = machineInfo.Select(x => new Machine
            {
                Hostname = x.Hostname,
                Cpu = x.MachineStats.Cpu,
                Memory = x.MachineStats.Memory
            }).ToList();
        }
        
        // Process containers
        if (containerInfo != null)
        {
            overview.Containers = containerInfo
                .SelectMany(x => x.Containers)
                .GroupBy(x => x.Name)
                .Select(x => x.ToList())
                .Select(x =>
                {
                    if (x.Count == 0) return null;
                    
                    var container = new Container
                    {
                        Name = x.First().Name,
                        Replicas = x.Count,
                        ExternalPort = x.First().ExternalPort
                    };
                    container.ContainerOnHostStatsList = containerInfo
                        .Where(y => y.Containers.Any(c => c.Name == container.Name))
                        .Select(y => 
                        {
                            var containerStats = y.Containers.First(c => c.Name == container.Name);
                            return new ContainerOnHostStats
                            {
                                Host = y.Hostname,
                                Cpu = containerStats.Cpu,
                                Memory = containerStats.Memory,
                            };
                        })
                        .ToList();
                    return container;
                })
                .Where(x => x != null)
                .ToList()!;
        }

        FileHelper.SetContentToFile("Assets/overview.json", overview, out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error writing overview to file: {errorMessage}");
    }
}