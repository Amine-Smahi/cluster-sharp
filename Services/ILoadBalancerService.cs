using ClusterSharp.Api.Services;

namespace ClusterSharp.Api.Services
{
    public interface ILoadBalancerService
    {
        /// <summary>
        /// Get the next available target host for the given source host
        /// </summary>
        /// <param name="sourceHost">The incoming request host</param>
        /// <param name="endpoints">List of available endpoints for this host</param>
        /// <returns>Selected target host or empty string if none available</returns>
        string GetNextEndpoint(string sourceHost, List<string> endpoints);
    }
} 