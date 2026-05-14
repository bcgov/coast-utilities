# Job Scheduling

This console application executes Dynamics scheduled jobs. It connects to Dynamics using either ADFS (on-premise) or Entra ID (cloud) authentication to trigger job execution.

## Configuration

### User Secrets Template

```json
{
  "DYNAMICS_JOB_NAME": "bcgov_example_job",
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
