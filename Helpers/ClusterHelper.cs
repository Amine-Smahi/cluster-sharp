using ClusterSharp.Api.Models;
using ClusterSharp.Api.Models.Cluster;
using ClusterNode = ClusterSharp.Api.Models.Cluster.ClusterNode;

namespace ClusterSharp.Api.Helpers;

public static class ClusterHelper
{
    public static ClusterSetup? GetClusterSetup()
    {
        var result = FileHelper.GetContentFromFile<ClusterSetup>("Assets/cluster.json", out var errorMessage);;
        if(result == null)
            Console.WriteLine(errorMessage);
        return result;
    }

    public static List<string> GetWorkers()
    {
        var result = FileHelper.GetContentFromFile<ClusterSetup>("Assets/cluster.json", out var errorMessage);
        if (result == null)
        {
            Console.WriteLine(errorMessage);
            return [];
        }

        return result.Members.Where(m => m.Role == Constants.Worker)
            .Select(x => x.Hostname)
            .ToList();

    }

    public static void GenerateClusterOverview(string clusterInfoPath)
    {
        var clusterInfo = FileHelper.GetContentFromFile<List<ClusterNode>>(clusterInfoPath, out var errorMessage);
        if (clusterInfo == null)
        {
            Console.WriteLine(errorMessage);
            return;
        }

        var containerMap = new Dictionary<string, List<(string Hostname, MachineStats MachineStats)>>();
        foreach (var node in clusterInfo)
        {
            foreach (var container in node.Containers)
            {
                if (!containerMap.ContainsKey(container.Name))
                    containerMap[container.Name] = [];
                containerMap[container.Name].Add((node.Hostname, node.MachineStats));
            }
        }

        var overview = new List<ClusterOverview>();
        foreach (var (containerName, hosts) in containerMap)
        {
            var orderedHosts = hosts
                .OrderBy(h => double.Parse(h.MachineStats.CPU))
                .ThenBy(h => h.MachineStats.Memory.Percentage)
                .ToList();
            
            overview.Add(new ClusterOverview
            {
                Name = containerName,
                Replicas = hosts.Count,
                Hosts = orderedHosts.Select(h => new HostInfo
                {
                    Hostname = h.Hostname,
                    CPU = h.MachineStats.CPU,
                    Memory = h.MachineStats.Memory.Value,
                    MemoryPercent = h.MachineStats.Memory.Percentage,
                    ExternalPort = clusterInfo.FirstOrDefault(n => n.Containers.Any(c => c.Name == containerName))?
                        .Containers.FirstOrDefault(c => c.Name == containerName)?.ExternalPort ?? string.Empty
                }).ToList()
            });
        }
        
        FileHelper.SetContentToFile("Assets/overview.json", overview, out errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error writing overview to file: {errorMessage}");
    }
}