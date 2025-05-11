using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Models.Overview;

namespace ClusterSharp.Api.Services;

public class ClusterOverviewService
{
    public ClusterOverview Overview { get; private set; } = null!;
    public event EventHandler? OverviewUpdated;
    
    private List<Node>? _machineInfo;
    private List<Node>? _containerInfo;
    
    public ClusterOverviewService()
    {
        LoadOverview();
        OverviewUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateOverview()
    {
        LoadOverview();
        OverviewUpdated?.Invoke(this, EventArgs.Empty);
    }
    
    public void UpdateMachineInfo()
    {
        _machineInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/machine-info.json", out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error loading machine info: {errorMessage}");
            
        GenerateOverview();
        OverviewUpdated?.Invoke(this, EventArgs.Empty);
    }
    
    public void UpdateContainerInfo()
    {
        _containerInfo = FileHelper.GetContentFromFile<List<Node>>("Assets/container-info.json", out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error loading container info: {errorMessage}");
            
        GenerateOverview();
        OverviewUpdated?.Invoke(this, EventArgs.Empty);
    }
    
    private void LoadOverview()
    {
        try
        {
            var result = FileHelper.GetContentFromFile<ClusterOverview>("Assets/overview.json", out var errorMessage);
            if (result != null)
                Overview = result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading overview file: {ex.Message}");
        }
    }
    
    private void GenerateOverview()
    {
        if (_machineInfo == null && _containerInfo == null)
            return;
            
        var overview = new ClusterOverview();
        if (_machineInfo != null)
        {
            overview.Machines = _machineInfo.Select(x => new Machine
            {
                Hostname = x.Hostname,
                Cpu = x.MachineStats.Cpu,
                Memory = x.MachineStats.Memory
            }).ToList();
        }
        if (_containerInfo != null)
        {
            overview.Containers = _containerInfo
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
        
        Overview = overview;
        FileHelper.SetContentToFile("Assets/overview.json", overview, out var errorMessage);
        if (errorMessage != null)
            Console.WriteLine($"Error writing overview to file: {errorMessage}");
    }
}