namespace ClusterSharp.Api.Models.Commands;

public class CommandResult
{
    public string? Command { get; set; }
    public string? Status { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}
