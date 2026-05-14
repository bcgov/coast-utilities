# AEMInterfaceService

This is a work in progress at this time.

This will be an interface between the Dynamics application and the AEM print server.

More to come...

## Configuration

### User Secrets Template

```json
{
  "ASPNETCORE_ENVIRONMENT": "Development",
  "ClientId": "your-cas-client-id",
  "ClientKey": "your-cas-client-key",
  "BaseUrl": "https://your-cas-instance/ords/cas",
  "TokenUrl": "https://your-cas-instance/ords/cas/oauth/token",
  "auth": {
    "instrospection": {
      "authority": "https://your-auth-server/auth/realms/standard",
      "clientid": "your-auth-client-id",
      "clientsecret": "your-auth-client-secret"
    },
    "jwt": {
      "authority": "https://your-auth-server/auth/realms/standard",
      "scope": "openid bceidbusiness email profile"
    },
    "oidc": {
      "clientid": "your-auth-client-id",
      "issuer": "https://your-auth-server/auth/realms/standard"
    }
  },
  "Dynamics": {
    "AuthenticationType": "OnPremise",
    "DynamicsApiEndpointUrl": "https://your-dynamics-instance.api.crm3.dynamics.com/api/data/v9.0/",
    "ADFS": {
      "OAuth2TokenEndpoint": "https://your-adfs-server/adfs/oauth2/token",
      "ClientId": "your-adfs-client-id",
      "ClientSecret": "your-adfs-client-secret",
      "ResourceName": "https://your-dynamics-instance.api.crm3.dynamics.com",
      "ServiceAccountName": "service-account@domain.com",
      "ServiceAccountPassword": "your-service-account-password"
    },
    "EntraId": {
      "TenantId": "your-tenant-id",
      "ClientId": "your-entra-client-id",
      "ClientSecret": "your-entra-client-secret",
      "ResourceName": "https://your-dynamics-instance.api.crm3.dynamics.com"
    }
  }
}
```
