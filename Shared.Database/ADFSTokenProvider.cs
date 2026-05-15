using System;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Database;

public class ADFSTokenProvider : ITokenProvider
{
    private const string cacheKey = "adfs_token";

    private readonly ADFSTokenProviderOptions options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ICache cache;
    private readonly ILogger<ADFSTokenProvider> logger;

    public ADFSTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<DynamicsTokenProviderOptions> options,
        ICache cache,
        ILogger<ADFSTokenProvider> logger
    )
    {
        this.options = options.Value.ADFS;
        this.httpClientFactory = httpClientFactory;
        this.cache = cache;
        this.logger = logger;
    }

    public async Task<string> AcquireToken() =>
        await cache.GetOrSet(cacheKey, AcquireTokenInternal, TimeSpan.FromMinutes(5)) ?? null!;

    private async Task<string> AcquireTokenInternal()
    {
        logger.LogDebug("Acquiring ADFS token from {0}", options.OAuth2TokenEndpoint);
        using var httpClient = httpClientFactory.CreateClient("adfs_token");

        var response = await httpClient.RequestPasswordTokenAsync(
            new PasswordTokenRequest
            {
                Address = options.OAuth2TokenEndpoint,
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
                Resource = { options.ResourceName },
                UserName = options.ServiceAccountName,
                Password = options.ServiceAccountPassword,
                Scope = "openid",
            }
        );

        if (response.IsError)
        {
            throw new InvalidOperationException(
                $"ADFS token request failed: {response.Error} - {response.ErrorDescription}"
            );
        }

        logger.LogInformation("ADFS token acquired");
        return response.AccessToken!;
    }
}
