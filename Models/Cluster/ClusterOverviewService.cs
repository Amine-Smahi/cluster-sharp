using ClusterSharp.Api.Helpers;

namespace ClusterSharp.Api.Models.Cluster;

public class ClusterOverviewService
{
    public List<ClusterOverview> Overview { get; private set; } = new();
    
    // Event that will be triggered when the overview is updated
    public event EventHandler? OverviewUpdated;
    
    public ClusterOverviewService()
    {
        LoadOverview();
    }
    
    public void UpdateOverview()
    {
        LoadOverview();
        // Trigger the event
        OverviewUpdated?.Invoke(this, EventArgs.Empty);
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