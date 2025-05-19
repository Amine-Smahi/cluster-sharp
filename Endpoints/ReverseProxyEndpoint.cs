using FastEndpoints;
using ClusterSharp.Api.Services;
using Microsoft.AspNetCore.Http.Extensions;
using System.Buffers;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.ObjectPool;

namespace ClusterSharp.Api.Endpoints;

public record struct ReverseProxyRequest
{
    public ReverseProxyRequest()
    {
        CatchAll = string.Empty;
    }
    
    public string CatchAll { get; set; }
}

public class ReverseProxyEndpoint(ClusterOverviewService overviewService, IHttpClientFactory httpClientFactory)
    : Endpoint<ReverseProxyRequest>
{
    private static readonly Random Random = new();
    private static readonly ObjectPool<HttpRequestMessage> RequestPool = new DefaultObjectPool<HttpRequestMessage>(new HttpRequestMessagePooledObjectPolicy());

    public override void Configure()
    {
        AllowAnonymous();
        Routes("/{*catchAll}");
        Verbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD");
        Options(b => b.WithOrder(int.MaxValue));
    }

    public override async Task HandleAsync(ReverseProxyRequest req, CancellationToken ct)
    {
        HttpRequestMessage? requestMessage = null;
        try
        {
            var container = overviewService.Overview.GetContainerForDomain(HttpContext.Request.Host.Value);
            if (container == null)
            {
                await SendNotFoundAsync(ct);
                return;
            }

            var hostIndex = Random.Next(0, container.ContainerOnHostStatsList.Count);
            var hostStats = container.ContainerOnHostStatsList[hostIndex];
            var port = container.ExternalPort;

            if (string.IsNullOrEmpty(port))
            {
                await SendOkAsync(cancellation: ct);
                return;
            }

            var hostValue = hostStats.Host;
            var originalUrl = HttpContext.Request.GetDisplayUrl();
            var uri = new Uri(originalUrl);
            var pathAndQuery = uri.PathAndQuery;
            
            var urlLength = "http://".Length + hostValue.Length + 1 + port.Length + pathAndQuery.Length;
            var targetUrl = BuildTargetUrl(urlLength, hostValue, port, pathAndQuery);
            
            requestMessage = RequestPool.Get();
            requestMessage.Method = new HttpMethod(HttpContext.Request.Method);
            requestMessage.RequestUri = new Uri(targetUrl);
            
            CopyHeaders(HttpContext.Request.Headers, requestMessage.Headers);
            if (HttpContext.Request.ContentLength > 0)
            {
                requestMessage.Content = new StreamContent(HttpContext.Request.Body);
                
                if (HttpContext.Request.ContentType != null)
                    requestMessage.Content.Headers.TryAddWithoutValidation("Content-Type", HttpContext.Request.ContentType);
            }
            
            var client = httpClientFactory.CreateClient("ReverseProxyClient");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            
            using var response = await client.SendAsync(
                requestMessage, 
                HttpCompletionOption.ResponseHeadersRead, 
                timeoutCts.Token).ConfigureAwait(false);
            
            HttpContext.Response.StatusCode = (int)response.StatusCode;
            
            CopyHeaders(response.Headers, HttpContext.Response.Headers);
            CopyHeaders(response.Content.Headers, HttpContext.Response.Headers);

            await response.Content.CopyToAsync(HttpContext.Response.Body, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await SendOkAsync(cancellation: ct);
        }
        finally
        {
            if (requestMessage != null)
            {
                requestMessage.Content?.Dispose();
                requestMessage.Content = null;
                RequestPool.Return(requestMessage);
            }
        }
    }

    private static string BuildTargetUrl(int urlLength, string hostValue, string port, string pathAndQuery)
    {
        return string.Create(urlLength, (hostValue, port, pathAndQuery), (span, state) =>
        {
            var position = 0;
            "http://".AsSpan().CopyTo(span);
            position += "http://".Length;
            state.hostValue.AsSpan().CopyTo(span[position..]);
            position += state.hostValue.Length;
            span[position++] = ':';
            state.port.AsSpan().CopyTo(span[position..]);
            position += state.port.Length;
            state.pathAndQuery.AsSpan().CopyTo(span[position..]);
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyHeaders(IHeaderDictionary source, System.Net.Http.Headers.HttpHeaders destination)
    {
        foreach (var header in source)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) || 
                header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            if (header.Value.Count == 1)
                destination.TryAddWithoutValidation(header.Key, header.Value[0]);
            else
                destination.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyHeaders(System.Net.Http.Headers.HttpHeaders source, IHeaderDictionary destination)
    {
        foreach (var header in source)
        {
            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;
            
            var values = header.Value.ToArray();
            destination[header.Key] = values.Length == 1 ? values[0] : values;
        }
    }
}


internal class HttpRequestMessagePooledObjectPolicy : PooledObjectPolicy<HttpRequestMessage>
{
    public override HttpRequestMessage Create() => new();

    public override bool Return(HttpRequestMessage obj)
    {
        obj.Content?.Dispose();
        obj.Content = null;
        obj.RequestUri = null;
        obj.Method = null!;
        obj.Headers.Clear();
        return true;
    }
}