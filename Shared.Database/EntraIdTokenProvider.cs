using System;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Database;

public class EntraIdTokenProvider : ITokenProvider
{
    private const string cacheKey = "entraid_token";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly EntraIdTokenProviderOptions options;
    private readonly ICache cache;
    private readonly ILogger<EntraIdTokenProvider> logger;

    public EntraIdTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<DynamicsTokenProviderOptions> options,
        ICache cache,
        ILogger<EntraIdTokenProvider> logger
    )
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options.Value.EntraId;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task<string> AcquireToken() =>
        await cache.GetOrSet(cacheKey, AcquireTokenInternal, TimeSpan.FromMinutes(5)) ?? null!;

    private async Task<string> AcquireTokenInternal()
    {
        var tokenEndpoint = $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token";
        logger.LogDebug("Acquiring Entra ID token from {0}", tokenEndpoint);

        using var httpClient = httpClientFactory.CreateClient("entraid_token");
        var response = await httpClient.RequestClientCredentialsTokenAsync(
            new ClientCredentialsTokenRequest
            {
                Address = tokenEndpoint,
                GrantType = "client_credentials",
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
                Scope = $"{options.ResourceName}/.default",
            }
        );

        if (response.IsError)
        {
            throw new InvalidOperationException(
                $"Entra ID token request failed: {response.Error} - {response.ErrorDescription}"
            );
        }

        logger.LogInformation("Entra ID token acquired");
        return response.AccessToken!;
    }
}
