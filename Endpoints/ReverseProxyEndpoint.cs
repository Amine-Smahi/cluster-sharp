using FastEndpoints;
using ClusterSharp.Api.Services;
using System.Runtime.CompilerServices;
using ZLinq;
using Yarp.ReverseProxy.Forwarder;

namespace ClusterSharp.Api.Endpoints;

public class ReverseProxyEndpoint(ClusterOverviewService overviewService, IHttpForwarder forwarder)
    : EndpointWithoutRequest
{
    private const int RequestTimeoutSeconds = 120;
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromSeconds(RequestTimeoutSeconds),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
        EnableMultipleHttp2Connections = false, // Considering we are proxying to HTTP/1.1 services usually
        MaxConnectionsPerServer = 80000, // High throughput
        UseCookies = false,
        UseProxy = false
    });

    public override void Configure()
    {
        AllowAnonymous();
        Routes("/{*catchAll}");
        Verbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD");
        Options(b => b.WithOrder(int.MaxValue));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var container = overviewService.Overview.GetContainerForDomain(HttpContext.Request.Host.Value);
            if (container == null || container.ContainerOnHostStatsList.Count == 0)
            {
                await SendNotFoundAsync(ct);
                return;
            }

            // Simple round-robin for now, YARP will handle load balancing
            var hostStats = container.ContainerOnHostStatsList[Random.Shared.Next(container.ContainerOnHostStatsList.Count)];
            var port = container.ExternalPort;

            if (string.IsNullOrEmpty(port))
            {
                await SendOkAsync(cancellation: ct);
                return;
            }

            var destinationPrefix = $"http://{hostStats.Host}:{port}";
            
            var error = await forwarder.SendAsync(HttpContext, destinationPrefix, _httpClient, new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) }, HttpTransformer.Default, ct);
            if (error != ForwarderError.None)
            {
                // Log error
                Console.WriteLine($"YARP forwarding error: {error}");
                HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await HttpContext.Response.WriteAsync("An error occurred while proxying the request.", ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Handled by YARP
            if (!ct.IsCancellationRequested) // If not client cancellation
            {
                HttpContext.Abort();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"error => {ex}");
            HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await HttpContext.Response.WriteAsync("An error occurred while proxying the request.", ct);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyHeaders(IHeaderDictionary source, System.Net.Http.Headers.HttpHeaders destination)
    {
        foreach (var header in source)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            destination.TryAddWithoutValidation(header.Key, (IEnumerable<string>)header.Value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyHeaders(System.Net.Http.Headers.HttpHeaders source, IHeaderDictionary destination)
    {
        foreach (var header in source)
        {
            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            var values = header.Value.AsValueEnumerable().ToArray();
            destination[header.Key] = values.Length == 1 ? values[0] : values;
        }
    }
}