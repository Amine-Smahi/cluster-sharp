using ClusterSharp.Api.Helpers;
using ClusterSharp.Api.Models.Overview;

namespace ClusterSharp.Api.Services;

public class ClusterOverviewService
{
    public ClusterOverview Overview { get; private set; } = null!;
    public event EventHandler? OverviewUpdated;
    
    public ClusterOverviewService()
    {
        LoadOverview();
        // Trigger the event initially to notify subscribers
        OverviewUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateOverview()
    {
        LoadOverview();
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
}