namespace Shared.Database;

public enum DynamicsAuthenticationType
{
    OnPremise,
    Cloud,
}

public class DynamicsTokenProviderOptions
{
    public DynamicsAuthenticationType AuthenticationType { get; set; } = DynamicsAuthenticationType.OnPremise;
    public ADFSTokenProviderOptions ADFS { get; set; } = new ADFSTokenProviderOptions();
    public EntraIdTokenProviderOptions EntraId { get; set; } = new EntraIdTokenProviderOptions();

    /// <summary>
    /// Gets the appropriate DynamicsApiEndpointUrl based on the current AuthenticationType
    /// </summary>
    public string GetDynamicsApiEndpointUrl()
    {
        return AuthenticationType switch
        {
            DynamicsAuthenticationType.OnPremise => ADFS?.DynamicsApiEndpointUrl ?? string.Empty,
            DynamicsAuthenticationType.Cloud => EntraId?.DynamicsApiEndpointUrl ?? string.Empty,
            _ => string.Empty,
        };
    }
}

public class ADFSTokenProviderOptions
{
    public string DynamicsApiEndpointUrl { get; set; } = string.Empty;
    public string OAuth2TokenEndpoint { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ServiceAccountName { get; set; } = string.Empty;
    public string ServiceAccountPassword { get; set; } = string.Empty;
}

public class EntraIdTokenProviderOptions
{
    public string DynamicsApiEndpointUrl { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
}
