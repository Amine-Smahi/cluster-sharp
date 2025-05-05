using System.Text.Json;

namespace ClusterSharp.Proxy;

public class ClusterOverviewService
{
    public List<ClusterOverview> Overview { get; private set; } = new();
    private readonly string _overviewFilePath;
    
    public ClusterOverviewService(string overviewFile)
    {
        _overviewFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", overviewFile);
        LoadOverview();
    }
    
    private void LoadOverview()
    {
        if (!File.Exists(_overviewFilePath))
        {
            Console.WriteLine($"Overview file not found: {_overviewFilePath}");
            return;
        }
        
        try
        {
            var jsonContent = File.ReadAllText(_overviewFilePath);
            var result = JsonSerializer.Deserialize<List<ClusterOverview>>(jsonContent);
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

public class ClusterOverview
{
    public string Name { get; set; } = string.Empty;
    public List<ClusterHost> Hosts { get; set; } = new();
}

public class ClusterHost
{
    public string Hostname { get; set; } = string.Empty;
    public int ExternalPort { get; set; }
} 