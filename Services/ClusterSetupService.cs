using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Cluster;

namespace ClusterSharp.Api.Services;

public class ClusterSetupService
{
    private Cluster? _cluster;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    public ClusterSetupService()
    {
        LoadClusterSetup();
    }
    
    public Cluster? GetCluster()
    {
        return _cluster;
    }
    
    public async Task<bool> UpdateClusterSetupAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return LoadClusterSetup();
        }
        finally
        {
            _semaphore.Release();
        }
    }
    
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