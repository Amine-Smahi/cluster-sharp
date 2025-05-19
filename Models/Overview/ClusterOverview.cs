using ZLinq;

namespace ClusterSharp.Api.Models.Overview;

public class ClusterOverview
{
    public List<Container> Containers { get; set; } = [];
    public List<Machine> Machines { get; set; } = [];

    public Container? GetContainerForDomain(string? domain) =>
        domain == null ? null : Containers
                .AsValueEnumerable()
                .FirstOrDefault(c => c.Name.Equals(domain, StringComparison.OrdinalIgnoreCase));
}