using FastEndpoints;
using ClusterSharp.Api.Services;
using Microsoft.AspNetCore.Http.Extensions;

namespace ClusterSharp.Api.Endpoints;

public record ReverseProxyRequest
{
    public string CatchAll { get; set; } = string.Empty;
}

public class ReverseProxyEndpoint(ClusterOverviewService overviewService, IHttpClientFactory httpClientFactory)
    : Endpoint<ReverseProxyRequest>
{
    public override void Configure()
    {
        AllowAnonymous();
        Routes("/{*catchAll}");
        Verbs("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS", "HEAD");
        Options(b => b.WithOrder(int.MaxValue));
    }

    public override async Task HandleAsync(ReverseProxyRequest req, CancellationToken ct)
    {
        try
        {
            var container = overviewService.Overview.GetContainerForDomain(HttpContext.Request.Host.Value);
            if (container == null)
            {
                await SendNotFoundAsync(ct);
                return;
            }
            
            var host = container.ContainerOnHostStatsList[0].Host;
            var port = container.ExternalPort;

            if (string.IsNullOrEmpty(port))
            {
                await SendOkAsync(cancellation: CancellationToken.None);
                return;
            }

            var originalUrl = HttpContext.Request.GetDisplayUrl();
            var uri = new Uri(originalUrl);

            var targetUrl = $"http://{host}:{port}{uri.PathAndQuery}";

            var requestMessage = new HttpRequestMessage
            {
                Method = new HttpMethod(HttpContext.Request.Method),
                RequestUri = new Uri(targetUrl)
            };

            foreach (var header in HttpContext.Request.Headers)
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());

            if (HttpContext.Request.ContentLength > 0)
            {
                var streamContent = new StreamContent(HttpContext.Request.Body);
                requestMessage.Content = streamContent;

                if (HttpContext.Request.ContentType != null)
                    requestMessage.Content.Headers.Add("Content-Type", HttpContext.Request.ContentType);
            }

            var client = httpClientFactory.CreateClient("ReverseProxyClient");
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            
            HttpContext.Response.StatusCode = (int)response.StatusCode;
            
            foreach (var header in response.Headers)
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    HttpContext.Response.Headers[header.Key] = header.Value.ToArray();

            foreach (var header in response.Content.Headers)
                HttpContext.Response.Headers[header.Key] = header.Value.ToArray();

            await response.Content.CopyToAsync(HttpContext.Response.Body, timeoutCts.Token);
        }
        catch (Exception)
        {
            await SendOkAsync(cancellation: CancellationToken.None);
        }
    }
}