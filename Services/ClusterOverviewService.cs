using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Overview;

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
            Overview.Machines = machineInfo.Select(x => new Machine
            {
                Hostname = x.Hostname,
                Cpu = x.MachineStats.Cpu,
                Memory = x.MachineStats.Memory
            }).ToList();
        }
        if (containerInfo != null)
        {
            Overview.Containers = containerInfo
                .SelectMany(x => x.Containers)
                .GroupBy(x => x.Name)
                .Select(x => x.ToList())
                .Select(x =>
                {
                    var container = new Container
                    {
                        Name = x.First().Name,
                        Replicas = x.Count,
                        ExternalPort = x.First().ExternalPort
                    };
                    container.ContainerOnHostStatsList = containerInfo
                        .Where(y => y.Containers.Any(c => c.Name == container.Name))
                        .OrderBy(y => y.MachineStats.Cpu)
                        .ThenBy(y => y.MachineStats.Memory)
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
                }).ToList();
        }
    }
}