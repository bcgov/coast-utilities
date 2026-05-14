# Coast Utilities - Shared Database Library

This library provides centralized authentication infrastructure for all Coast Utilities projects.

## Usage

### 1. Add Project Reference

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\Database\Database.csproj" />
</ItemGroup>
```

### 2. Configure Secrets

Configuration uses a nested structure under the `Dynamics` section:

#### ADFS (On-Premise) Configuration

```json
{
  "Dynamics": {
    "AuthenticationType": "OnPremise",
    "DynamicsApiEndpointUrl": "https://your-dynamics-instance.example.com/api/data/v9.2/",
    "ADFS": {
      "OAuth2TokenEndpoint": "https://adfs.example.com/adfs/oauth2/token",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "ResourceName": "https://your-dynamics-instance.example.com",
      "ServiceAccountName": "service-account-username",
      "ServiceAccountPassword": "service-account-password"
    }
  }
}
```

#### Entra ID (Cloud) Configuration

```json
{
  "Dynamics": {
    "AuthenticationType": "Cloud",
    "DynamicsApiEndpointUrl": "https://your-org.crm.dynamics.com/api/data/v9.2/",
    "EntraId": {
      "TenantId": "your-tenant-id",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "ResourceName": "https://your-org.crm.dynamics.com/"
    }
  }
}
```
