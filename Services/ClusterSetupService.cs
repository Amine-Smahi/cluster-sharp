using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;

namespace ClusterSharp.Api.Services;

public class ClusterSetupService
{
    private Cluster? _cluster;

    public ClusterSetupService() => LoadClusterSetup();

    public Cluster? GetCluster() => _cluster;

    public bool UpdateClusterSetupAsync() => LoadClusterSetup();

    private bool LoadClusterSetup()
    {
        var cluster = FileHelper.GetContentFromFile<Cluster>("Assets/cluster-setup.json", out var errorMessage);
        if (cluster == null)
        {
            Console.WriteLine($"Error loading cluster setup: {errorMessage}");
            return false;
        }

        _cluster = cluster;
        return true;
    }
}