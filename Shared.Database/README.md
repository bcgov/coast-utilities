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
      "ClientId": "<onpremise_client_id>",
      "ClientSecret": "<onpremise_client_secret>",
      "ServiceAccountName": "<onpremise_service_account_username>",
      "ServiceAccountPassword": "<onpremise_service_account_password",
      "ResourceName": "https://cscp-vs.dev.jag.gov.bc.ca/api/data/v9.0/"
    },
    "EntraId": {
      "DynamicsApiEndpointUrl": "https://csvs-coast-dev.api.crm3.dynamics.com/api/data/v9.2/",
      "TenantId": "<cloud_tenant_id>",
      "ClientId": "<cloud_client_id>",
      "ClientSecret": "<cloud_client_secret>",
      "ResourceName": "https://csvs-coast-dev.crm3.dynamics.com"
    }
  }
}
```
