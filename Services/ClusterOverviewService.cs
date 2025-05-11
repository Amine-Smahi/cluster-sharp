using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Overview;

namespace ClusterSharp.Api.Services;

public class ClusterOverviewService
{
    public ClusterOverview Overview { get; } = new ();

    private List<Node>? _machineInfo;
    private List<Node>? _containerInfo;
    
    public void UpdateMachineInfo()
    {
        _machineInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/machine-info.json", out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error loading machine info: {errorMessage}");
            
        GenerateOverview();
    }
    
    public void UpdateContainerInfo()
    {
        _containerInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/container-info.json", out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error loading container info: {errorMessage}");
            
        GenerateOverview();
    }
    
    private void GenerateOverview()
    {
        if (_machineInfo == null && _containerInfo == null)
            return;
            
        if (_machineInfo != null)
        {
            Overview.Machines = _machineInfo.Select(x => new Machine
            {
                Hostname = x.Hostname,
                Cpu = x.MachineStats.Cpu,
                Memory = x.MachineStats.Memory
            }).ToList();
        }
        if (_containerInfo != null)
        {
            Overview.Containers = _containerInfo
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
                    container.ContainerOnHostStatsList = _containerInfo
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
                }).ToList();
        }
    }
}