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
        var clusterInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/cluster-info.json", out var errorMessage);
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

        var overview = new ClusterOverview
        {
            Containers = clusterInfo.SelectMany(x => x.Containers)
                .GroupBy(x => x.Name)
                .Select(x => x.ToList())
                .Select(x =>
                {
                    var container = new Container
                    {
                        Name = x.First().Name,
                        Replicas = x.Count(),
                        ExternalPort = x.First().ExternalPort,
                    };
                    container.Hosts = clusterInfo.Where(y => y.Containers.Any(c => c.Name == container.Name))
                        .Select(y => y.Hostname).Distinct().ToList();
                    return container;
                }).ToList(),
            Machines = clusterInfo.Select(x => new Machine
            {
                Hostname = x.Hostname,
                Cpu = x.MachineStats.Cpu,
                Memory = x.MachineStats.Memory
            }).ToList()
        };

        FileHelper.SetContentToFile("Assets/overview.json", overview, out errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error writing overview to file: {errorMessage}");
    }
}