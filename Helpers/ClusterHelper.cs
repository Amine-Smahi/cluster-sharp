using ClusterSharp.Api.Models.Cluster;
using ClusterSharp.Api.Services;
using ClusterSharp.Api.Shared;

namespace ClusterSharp.Api.Helpers;

public static class ClusterHelper
{
    private static ClusterSetupService? _clusterSetupService;
    
    public static void Initialize(IServiceProvider serviceProvider)
    {
        _clusterSetupService = serviceProvider.GetRequiredService<ClusterSetupService>();
    }
    
    public static Cluster? GetClusterSetup()
    {
        if (_clusterSetupService != null)
        {
            var cluster = _clusterSetupService.GetCluster();
            if (cluster != null)
                return cluster;
        }
        
        var result = FileHelper.GetContentFromFile<Cluster>("Assets/cluster-setup.json", out var errorMessage);
        if (result == null)
            Console.WriteLine(errorMessage);
        return result;
    }

    public static List<string> GetWorkers()
    {
        var result = GetClusterSetup();
        if (result == null)
        {
            Console.WriteLine("Failed to get cluster setup");
            return [];
        }

        return result.Nodes.Where(m => m.Role == Constants.Worker)
            .Select(x => x.Hostname)
            .ToList();
    }
}