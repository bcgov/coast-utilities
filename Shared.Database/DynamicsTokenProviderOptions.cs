namespace Shared.Database;

public enum DynamicsAuthenticationType
{
    OnPremise,
    Cloud,
}

public class DynamicsTokenProviderOptions
{
    public DynamicsAuthenticationType AuthenticationType { get; set; } =
        DynamicsAuthenticationType.OnPremise;
    public string DynamicsApiEndpointUrl { get; set; } = string.Empty;
    public ADFSTokenProviderOptions ADFS { get; set; } = new();
    public EntraIdTokenProviderOptions EntraId { get; set; } = new();
}

public class ADFSTokenProviderOptions
{
    public string OAuth2TokenEndpoint { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ServiceAccountName { get; set; } = string.Empty;
    public string ServiceAccountPassword { get; set; } = string.Empty;
}

public class EntraIdTokenProviderOptions
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
}
