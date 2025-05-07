namespace ClusterSharp.Api.Models.Overview;

public class ContainerOnHostStats
{
    public string Host { get; set; } = null!;
    public double Cpu { get; set; }
    public double Memory { get; set; }
}