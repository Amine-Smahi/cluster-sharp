using ClusterSharp.Api.Helpers;

namespace ClusterSharp.Api.Models.Cluster;

public class ClusterOverviewService
{
    public ClusterOverview Overview { get; private set; } = null!;
    public event EventHandler? OverviewUpdated;
    
    public ClusterOverviewService() => LoadOverview();

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