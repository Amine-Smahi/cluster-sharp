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
            
            // Track if cancellation has occurred
            bool isCancellationRequested = false;
            
            // Create a token source with a longer timeout and monitor the request
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));
            
            // Create a linked token that will cancel if either the original token or our timeout token cancels
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
            
            // Register a callback to identify if the original request token was cancelled
            using var clientDisconnectReg = ct.Register(() => {
                isCancellationRequested = true;
                Console.WriteLine($"Original client request cancelled for {targetUrl}");
            });
            
            try
            {
                // Monitor for pending cancellation
                if (linkedCts.Token.IsCancellationRequested)
                {
                    Console.WriteLine($"Request already cancelled before sending for {targetUrl}");
                    return;
                }
                
                var response = await client.SendAsync(
                    requestMessage, 
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token);
                
                // Check if client disconnected during the request
                if (isCancellationRequested || ct.IsCancellationRequested)
                {
                    Console.WriteLine($"Client disconnected after receiving response headers for {targetUrl}");
                    return;
                }
                
                HttpContext.Response.StatusCode = (int)response.StatusCode;
                
                foreach (var header in response.Headers)
                    if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) 
                        HttpContext.Response.Headers[header.Key] = header.Value.ToArray();

                foreach (var header in response.Content.Headers) 
                    HttpContext.Response.Headers[header.Key] = header.Value.ToArray();
                
                // Stream the response directly instead of loading it into memory
                await response.Content.CopyToAsync(HttpContext.Response.Body, linkedCts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
            {
                // Client cancelled or timeout - just return OK
                await SendOkAsync(cancellation: CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Non-cancellation error: {ex.Message}");
                await SendAsync("Bad Gateway", StatusCodes.Status502BadGateway, cancellation: CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException || ex is TaskCanceledException)
            {
                // Global cancellation - just return OK
                await SendOkAsync(cancellation: CancellationToken.None);
                return;
            }
            
            // Add better diagnostics for other error cases
            Console.WriteLine($"Global proxy error: {ex.Message}");
            await SendAsync("Internal Server Error", StatusCodes.Status500InternalServerError, cancellation: CancellationToken.None);
        }
    }
}

public class ReverseProxyRequest
{
    public string CatchAll { get; set; } = string.Empty;
} 