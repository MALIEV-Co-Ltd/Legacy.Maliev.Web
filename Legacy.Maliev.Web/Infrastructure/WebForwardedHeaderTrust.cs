using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Infrastructure;

/// <summary>Requires an explicit proxy or network trust entry before Web processes forwarded headers.</summary>
internal static class WebForwardedHeaderTrust
{
    internal static IServiceCollection AddWebForwardedHeaderTrust(this IServiceCollection services)
    {
        services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            // Empty lists disable the framework's peer check rather than rejecting every peer.
            if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            {
                options.ForwardedHeaders = ForwardedHeaders.None;
            }
        });
        return services;
    }
}
