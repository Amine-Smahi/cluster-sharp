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
                await SendAsync("Service Unavailable", StatusCodes.Status503ServiceUnavailable, cancellation: ct);
                return;
            }
            
            var originalUrl = HttpContext.Request.GetDisplayUrl();
            var uri = new Uri(originalUrl);
            
            var targetUrl = $"http://{host}:{port}{uri.PathAndQuery}";
            //Console.WriteLine($"Proxying request to: {targetUrl}");
            
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
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180)); // Increased timeout
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
            
            try
            {
                var response = await client.SendAsync(
                    requestMessage, 
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token);
                
                HttpContext.Response.StatusCode = (int)response.StatusCode;
                
                foreach (var header in response.Headers)
                    if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) 
                        HttpContext.Response.Headers[header.Key] = header.Value.ToArray();

                foreach (var header in response.Content.Headers) 
                    HttpContext.Response.Headers[header.Key] = header.Value.ToArray();
                
                // Stream the response directly instead of loading it into memory
                await response.Content.CopyToAsync(HttpContext.Response.Body, linkedCts.Token);
            }
            catch (TaskCanceledException ex)
            {
                if (cts.Token.IsCancellationRequested) 
                {
                    Console.WriteLine($"Proxy request timed out: {targetUrl}");
                    await SendAsync("Gateway Timeout", StatusCodes.Status504GatewayTimeout, cancellation: ct);
                }
                else if (ct.IsCancellationRequested)
                {
                    // Request canceled by client
                }
                else
                {
                    Console.WriteLine($"Proxy request canceled: {ex.Message}");
                    await SendAsync("Bad Gateway", StatusCodes.Status502BadGateway, cancellation: ct);
                }
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException && ct.IsCancellationRequested)
                    return;
                
                Console.WriteLine($"Proxy error: {ex.Message}");
                await SendAsync("Bad Gateway", StatusCodes.Status502BadGateway, cancellation: ct);
            }
        }
        catch (Exception ex)
        {
            if (ex is TaskCanceledException && ct.IsCancellationRequested)
                return;
            
            Console.WriteLine($"Global proxy error: {ex.Message}");
            await SendAsync("Internal Server Error", StatusCodes.Status500InternalServerError, cancellation: ct);
        }
    }
}

public class ReverseProxyRequest
{
    public string CatchAll { get; set; } = string.Empty;
} 