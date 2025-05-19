using FastEndpoints;

namespace ClusterSharp.Api.Helpers;

public static class ReverseProxyExtensions
{
    public static IApplicationBuilder UseReverseProxy(this IApplicationBuilder app)
    {
        app.UseFastEndpoints(config => 
        {
            config.Serializer.Options.PropertyNamingPolicy = null;
            config.Endpoints.Configurator = ep => 
            {
                if (ep.EndpointType.Name == "ReverseProxyEndpoint") 
                    ep.Options(x => x.WithOrder(int.MaxValue));
            };
        });
        
        return app;
    }
} 