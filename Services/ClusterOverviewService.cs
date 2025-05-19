using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Overview;
using ZLinq;

namespace ClusterSharp.Api.Services;

public class ClusterOverviewService
{
    public ClusterOverview Overview { get; } = new ();
    
    public void UpdateMachineInfo()
    {
        var machineInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/machine-info.json", out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error loading machine info: {errorMessage}");
            
        GenerateOverview(machineInfo, null);
    }
    
    public void UpdateContainerInfo()
    {
        var containerInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/container-info.json", out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error loading container info: {errorMessage}");
            
        GenerateOverview(null, containerInfo);
    }
    
    private void GenerateOverview(List<Node>? machineInfo, List<Node>? containerInfo)
    {
        if (machineInfo == null && containerInfo == null)
            return;
            
        if (machineInfo != null)
        {
            Overview.Machines = machineInfo.AsValueEnumerable().Select(x => new Machine
            {
                Hostname = x.Hostname,
                Cpu = x.MachineStats.Cpu,
                Memory = x.MachineStats.Memory
            }).ToList();
        }
        if (containerInfo != null)
        {
            Overview.Containers = containerInfo
                .AsValueEnumerable()
                .SelectMany(x => x.Containers)
                .GroupBy(x => x.Name)
                .Select(x => x.AsValueEnumerable().ToList())
                .Select(x =>
                {
                    var container = new Container
                    {
                        Name = x.AsValueEnumerable().First().Name,
                        Replicas = x.Count,
                        ExternalPort = x.AsValueEnumerable().First().ExternalPort
                    };
                    container.ContainerOnHostStatsList = containerInfo
                        .AsValueEnumerable()
                        .Where(y => y.Containers.AsValueEnumerable().Any(c => c.Name == container.Name))
                        .Select(y => 
                        {
                            var containerStats = y.Containers.AsValueEnumerable().First(c => c.Name == container.Name);
                            return new ContainerOnHostStats
                            {
                                Host = y.Hostname,
                                Cpu = containerStats.Cpu,
                                Memory = containerStats.Memory,
                            };
                        })
                        .ToList();
                    return container;
                }).ToList();
        }
    }
}