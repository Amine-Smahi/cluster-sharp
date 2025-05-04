using System.Text.Json;
using ClusterSharp.Api.Models;
using ClusterSharp.Api.Models.Cluster;
using ClusterNode = ClusterSharp.Api.Models.Cluster.ClusterNode;

namespace ClusterSharp.Api.Helpers;

public static class ClusterHelper
{
    public static List<ClusterNode>? GetClusterInfo()
    {
        var result = FileHelper.GetContentFromFile<List<ClusterNode>>("Assets/cluster-info.json", out var errorMessage);
        if(result == null)
            Console.WriteLine(errorMessage);
        return result;
    }
    
    public static ClusterSetup? GetClusterSetup()
    {
        var result = FileHelper.GetContentFromFile<ClusterSetup>("Assets/cluster.json", out var errorMessage);;
        if(result == null)
            Console.WriteLine(errorMessage);
        return result;
    }

    public static List<string> GetWorkers()
    {
        var clusterInfo = GetClusterInfo();
        return clusterInfo?.Where(m => m.Role == Constants.Worker)
            .OrderBy(x => x.MachineStats.CPU)
            .ThenBy(x => x.MachineStats.Memory.Percentage)
            .Select(m => m.Hostname).ToList() ?? [];
    }

    public static void GenerateClusterOverview(string clusterInfoPath)
    {
        var clusterInfoJson = File.ReadAllText(clusterInfoPath);
        var clusterInfo = JsonSerializer.Deserialize<List<ClusterNode>>(clusterInfoJson);
        if (clusterInfo == null) return;

        var containerMap = new Dictionary<string, List<(string Hostname, Models.MachineStats MachineStats)>>();
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
        
        FileHelper.SetContentToFile("Assets/overview.json", overview, out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error writing overview to file: {errorMessage}");
    }
}