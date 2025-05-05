using ClusterSharp.Api.Helpers;

namespace ClusterSharp.Api.Models.Cluster;

public class ClusterOverviewService
{
    public List<ClusterOverview> Overview { get; private set; } = new();
    
    public ClusterOverviewService()
    {
        LoadOverview();
    }
    
    private void LoadOverview()
    {
        try
        {
            var result = FileHelper.GetContentFromFile<List<ClusterOverview>>("Assets/overview.json", out var errorMessage);
            if (result != null)
            {
                Overview = result;
                Console.WriteLine($"Loaded {Overview.Count} clusters from overview file");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading overview file: {ex.Message}");
        }
    }
}