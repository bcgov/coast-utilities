using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Database;

/// <summary>
/// DI-friendly registration for <see cref="ITokenProvider"/>.
///
/// Unlike <see cref="DynamicsAuthHelper.CreateTokenProvider"/> (which constructs a brand-new
/// <see cref="Microsoft.Extensions.Caching.Memory.MemoryCache"/> and <see cref="ILoggerFactory"/>
/// on every call, silently defeating the provider's 5-minute token cache), this registers the
/// token provider as a long-lived singleton backed by the application's shared memory cache and
/// logging infrastructure, so the cached token is actually reused across requests.
/// </summary>
public static class DynamicsAuthServiceCollectionExtensions
{
    public static IServiceCollection AddDynamicsTokenProvider(
        this IServiceCollection services,
        string configurationSectionName = "Dynamics"
    )
    {
        services.AddMemoryCache();
        services.AddSingleton<ICache, MemoryCache>();

        services
            .AddOptions<DynamicsTokenProviderOptions>()
            .Configure<IConfiguration>(
                (options, config) => config.GetSection(configurationSectionName).Bind(options)
            );

        services.AddSingleton<ITokenProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DynamicsTokenProviderOptions>>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var cache = sp.GetRequiredService<ICache>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            return options.Value.AuthenticationType switch
            {
                DynamicsAuthenticationType.Cloud => new EntraIdTokenProvider(
                    httpClientFactory,
                    options,
                    cache,
                    loggerFactory.CreateLogger<EntraIdTokenProvider>()
                ),
                DynamicsAuthenticationType.OnPremise => new ADFSTokenProvider(
                    httpClientFactory,
                    options,
                    cache,
                    loggerFactory.CreateLogger<ADFSTokenProvider>()
                ),
                _ => throw new InvalidOperationException(
                    $"Unknown authentication type: {options.Value.AuthenticationType}"
                ),
            };
        });

        return services;
    }
}

