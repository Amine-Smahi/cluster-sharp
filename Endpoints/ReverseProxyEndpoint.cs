using FastEndpoints;
using ClusterSharp.Api.Services;
using System.Runtime.CompilerServices;
using ZLinq;

namespace ClusterSharp.Api.Endpoints;

public class ReverseProxyEndpoint(ClusterOverviewService overviewService, IHttpClientFactory httpClientFactory)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        AllowAnonymous();
        Routes("/{*catchAll}");
        Verbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD");
        Options(b => b.WithOrder(int.MaxValue));
    }

    public override async Task HandleAsync(CancellationToken ct)
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

            var hostIndex = Random.Shared.Next(0, container.ContainerOnHostStatsList.Count);
            var hostStats = container.ContainerOnHostStatsList[hostIndex];
            var port = container.ExternalPort;

            if (string.IsNullOrEmpty(port))
            {
                await SendOkAsync(cancellation: ct);
                return;
            }

            var currentRequest = HttpContext.Request;
            var pathAndQuery = currentRequest.Path.ToUriComponent() + currentRequest.QueryString.ToUriComponent();
            var targetUri = new Uri($"http://{hostStats.Host}:{port}{pathAndQuery}");

            requestMessage = new HttpRequestMessage
            {
                Method = new HttpMethod(currentRequest.Method),
                RequestUri = targetUri
            };

            CopyHeaders(currentRequest.Headers, requestMessage.Headers);
            if (currentRequest.ContentLength > 0)
            {
                requestMessage.Content = new StreamContent(currentRequest.Body);

                if (currentRequest.ContentType != null)
                    requestMessage.Content.Headers.TryAddWithoutValidation("Content-Type",
                        currentRequest.ContentType);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            using var response = await httpClientFactory.CreateClient("ReverseProxyClient")
                .SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            HttpContext.Response.StatusCode = (int)response.StatusCode;

            CopyHeaders(response.Headers, HttpContext.Response.Headers);
            CopyHeaders(response.Content.Headers, HttpContext.Response.Headers);

            await response.Content.CopyToAsync(HttpContext.Response.Body, timeoutCts.Token).ConfigureAwait(false);
        }
        catch
        {
            HttpContext.Response.StatusCode = StatusCodes.Status200OK;
            await HttpContext.Response.WriteAsync("An error occurred while proxying the request.", ct);
        }
        finally
        {
            requestMessage?.Dispose();
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