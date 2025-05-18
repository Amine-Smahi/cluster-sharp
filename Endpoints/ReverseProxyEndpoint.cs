using FastEndpoints;
using ClusterSharp.Api.Services;
using Microsoft.AspNetCore.Http.Extensions;
using System.Net.Http;
using System.IO;
using System.Net;

namespace ClusterSharp.Api.Endpoints;

public class ReverseProxyEndpoint(ClusterOverviewService overviewService, IHttpClientFactory httpClientFactory) : Endpoint<ReverseProxyRequest>
{
    private static readonly Random Random = new();
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
            
            var hostIndex = Random.Next(0, container.ContainerOnHostStatsList.Count);
            var host = container.ContainerOnHostStatsList[hostIndex].Host;
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
            
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                
                var response = await client.SendAsync(
                    requestMessage, 
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);
                
                // Set the status code from the response
                HttpContext.Response.StatusCode = (int)response.StatusCode;
                
                // Copy the headers from the response
                foreach (var header in response.Headers)
                    if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) 
                        HttpContext.Response.Headers[header.Key] = header.Value.ToArray();

                foreach (var header in response.Content.Headers) 
                    HttpContext.Response.Headers[header.Key] = header.Value.ToArray();
                
                // Copy the content from the response
                try
                {
                    await response.Content.CopyToAsync(HttpContext.Response.Body, timeoutCts.Token);
                }
                catch (Exception)
                {
                    // If streaming the content fails, at least return what we have so far
                    // The status code and headers are already set
                }
            }
            catch (Exception)
            {
                // Any error at all - just return OK
                await SendOkAsync(cancellation: CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Any error at all - just return OK
            await SendOkAsync(cancellation: CancellationToken.None);
        }
    }
}

public class ReverseProxyRequest
{
    public string CatchAll { get; set; } = string.Empty;
} 