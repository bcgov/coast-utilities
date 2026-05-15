# Coast Utilities - Shared Database Library

This library provides centralized authentication infrastructure for all Coast Utilities projects.

## Secrets Template

```JSON
{
  "Dynamics": {
    "AuthenticationType": "OnPremise", // "Cloud" or "OnPremise"
    "ADFS": {
      "DynamicsApiEndpointUrl": "https://wsgw.dev.jag.gov.bc.ca/victim/api/data/v9.0/",
      "OAuth2TokenEndpoint": "https://ststest.gov.bc.ca/adfs/oauth2/token",
      "ClientId": "<client_id>",
      "ClientSecret": "<client_secret>",
      "ServiceAccountName": "<service_account_username>",
      "ServiceAccountPassword": "<service_account_password",
      "ResourceName": "https://cscp-vs.dev.jag.gov.bc.ca/api/data/v9.0/"
    },
    "EntraId": {
      "DynamicsApiEndpointUrl": "https://cscp-dev.api.crm3.dynamics.com/api/data/v9.2/",
      "TenantId": "<tenant_id>",
      "ClientId": "<client_id>",
      "ClientSecret": "<client_secret>",
      "ResourceName": "https://cscp-dev.api.crm3.dynamics.com"
    }
  }
}
```
