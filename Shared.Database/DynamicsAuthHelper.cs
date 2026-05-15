using System;
using System.Net.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Database;

public static class DynamicsAuthHelper
{
    public static ITokenProvider CreateTokenProvider(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        // Bind configuration to DynamicsTokenProviderOptions
        var dynamicsOptions =
            config.GetSection("Dynamics").Get<DynamicsTokenProviderOptions>() ?? new DynamicsTokenProviderOptions();

        // Create dependencies
        var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryCache(memoryCache);
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

        // Wrap options for IOptions<T>
        var options = Options.Create(dynamicsOptions);

        return dynamicsOptions.AuthenticationType switch
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
                $"Unknown authentication type: {dynamicsOptions.AuthenticationType}"
            ),
        };
    }
}
