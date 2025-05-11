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
}