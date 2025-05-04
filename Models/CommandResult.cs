namespace ClusterSharp.Api.Models;

public class CommandResult
{
    public string Command { get; set; }
    public string Status { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }
}
