# CASInterfaceService — Comprehensive Documentation

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Authentication & Authorization](#authentication--authorization)
4. [Configuration Reference](#configuration-reference)
5. [API Endpoints](#api-endpoints)
   - [CASAPRetreive](#casapretrieve)
   - [CASAPTransaction](#casaptransaction)
   - [CASSupplierRetreive](#cassupplierretrieve)
   - [CornetTransaction](#cornettransaction)
6. [Data Models](#data-models)
   - [CASAPTransaction](#casaptransaction-model)
   - [InvoiceLineDetail](#invoicelinedetail-model)
   - [CASAPQuery](#casapquery-model)
   - [CASSupplierQuery](#cassupplierquery-model)
   - [CornetTransaction](#cornettransaction-model)
   - [CornetTransactionEventData](#cornettransactioneventdata-model)
   - [CornetDynamicsModel](#cornetdynamicsmodel)
7. [CAS OAuth Token Flow](#cas-oauth-token-flow)
8. [CORNET-to-Dynamics Integration](#cornet-to-dynamics-integration)
9. [Logging](#logging)
10. [Deployment](#deployment)
11. [Local Development](#local-development)
12. [Postman Collection](#postman-collection)

---

## Overview

**CASInterfaceService** is a BC Government ASP.NET Core REST API that acts as a middleware bridge between BC Government systems and the Common Accounts System (CAS). It also relays CORNET event notifications to Microsoft Dynamics 365.

Key responsibilities:

| Concern               | Description                                                                                                          |
| --------------------- | -------------------------------------------------------------------------------------------------------------------- |
| AP Invoice Submission | Accepts AP invoice payloads from upstream systems and forwards them to the CAS ORDS API                              |
| AP Invoice Retrieval  | Queries the CAS ORDS API for existing invoice status and payment information                                         |
| Supplier Lookup       | Queries CAS for supplier registration data                                                                           |
| CORNET Notifications  | Receives CORNET event notifications and forwards them to Dynamics 365 via the `vsd_CreateCORNETNotifications` action |

---

## Architecture

```
┌──────────────────────────┐
│  Upstream BC Gov System  │
└────────────┬─────────────┘
             │  JWT-authenticated REST calls
             ▼
┌──────────────────────────────────────────────────────┐
│               CASInterfaceService                    │
│  ASP.NET Core · Port 8080 · .NET 10                  │
│                                                      │
│  ┌────────────────┐  ┌────────────────────────────┐  │
│  │ CASAPRetreive  │  │   CASAPTransaction         │  │
│  │ Controller     │  │   Controller               │  │
│  └───────┬────────┘  └────────────┬───────────────┘  │
│          │                        │                  │
│  ┌───────┴────────────────────────┴───────────────┐  │
│  │         CAS ORDS API (OAuth 2.0 client_creds)  │  │
│  └────────────────────────────────────────────────┘  │
│                                                      │
│  ┌──────────────────────┐  ┌────────────────────┐    │
│  │ CASSupplierRetreive  │  │ CornetTransaction  │    │
│  │ Controller           │  │ Controller         │    │
│  └──────────┬───────────┘  └────────┬───────────┘    │
│             │                       │                │
│  ┌──────────┴──────────┐  ┌─────────┴─────────────┐  │
│  │  CAS ORDS API       │  │  Dynamics 365 OData   │  │
│  └─────────────────────┘  └───────────────────────┘  │
└──────────────────────────────────────────────────────┘
```

- **Runtime**: ASP.NET Core hosted on Kestrel, bound to `http://*:8080`
- **Container base**: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`
- **Platform**: OpenShift (namespace `pssg-cscp-vsd`)

---

## Authentication & Authorization

All API controllers are decorated with `[Authorize]`. The service uses JWT Bearer authentication issued by a **BC Gov Keycloak** realm.

### Conditional Authentication

Authentication can be toggled via configuration for non-production use:

| Config key           | Type     | Effect                                                                                                               |
| -------------------- | -------- | -------------------------------------------------------------------------------------------------------------------- |
| `auth:jwt:enabled`   | `bool`   | When `false`, all requests are allowed through without a JWT. When `true` (default intent), a valid JWT is required. |
| `auth:jwt:authority` | `string` | Keycloak realm base URL (e.g., `https://keycloak.example.com/auth/realms/standard`)                                  |
| `auth:jwt:audience`  | `string` | Expected JWT `aud` claim value                                                                                       |

The `ConditionalAuthorizationHandler` implements this logic. When `auth:jwt:enabled` is `false`, it immediately succeeds all authorization requirements and logs a debug message. When enabled, the standard JWT validation runs:

- Issuer signing key validated against the authority
- Authentication failures and successes are logged via Serilog

### Obtaining a Token (Client Credentials)

Clients must obtain a JWT from Keycloak using the OAuth 2.0 `client_credentials` grant:

```http
POST {{tokenUrl}}
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id={{clientId}}&client_secret={{clientSecret}}
```

Include the token in subsequent requests:

```http
Authorization: Bearer <access_token>
```

---

## Configuration Reference

Settings are loaded from `appsettings.json`, environment-specific `appsettings.{env}.json`, environment variables, and .NET User Secrets (development only).

| Key                               | Required                | Description                                                                                                                                        |
| --------------------------------- | ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CAS_API_URI`                     | Yes                     | Base URL of the CAS ORDS API (e.g., `https://<server>:<port>/ords/casords/`). Paths `cfs/apinvoice/` and `oauth/token` are appended automatically. |
| `auth:jwt:authority`              | Yes (when auth enabled) | Keycloak realm authority URL                                                                                                                       |
| `auth:jwt:audience`               | Yes (when auth enabled) | JWT audience claim                                                                                                                                 |
| `auth:jwt:enabled`                | Yes                     | Set `true` to enforce JWT auth; `false` to allow unauthenticated access                                                                            |
| `Dynamics:ServiceAccountDomain`   | Conditional             | Dynamics 365 authentication domain                                                                                                                 |
| `Dynamics:ServiceAccountName`     | Conditional             | Dynamics 365 service account username                                                                                                              |
| `Dynamics:ServiceAccountPassword` | Conditional             | Dynamics 365 service account password                                                                                                              |
| `Dynamics:OAuthUrl`               | Conditional             | OAuth URL for acquiring a Dynamics token                                                                                                           |
| `Dynamics:ResourceUrl`            | Conditional             | Dynamics 365 resource URL                                                                                                                          |
| `Dynamics:ApiEndpointUrl`         | Conditional             | OData API endpoint base URL                                                                                                                        |
| `SPLUNK_COLLECTOR_URL`            | No                      | Splunk HEC collector URL. If omitted, console-only logging is used.                                                                                |
| `SPLUNK_TOKEN`                    | No                      | Splunk HEC token                                                                                                                                   |
| `ASPNETCORE_ENVIRONMENT`          | No                      | `Development` enables verbose logging and relaxes SSL for Splunk                                                                                   |

---

## API Endpoints

All endpoints return JSON. All endpoints (except `/hc`) require a valid JWT unless `auth:jwt:enabled` is `false`.

The health check endpoint is unauthenticated and mapped at `/hc`.

---

### CASAPRetreive

Base route: `api/CASAPRetreive`

#### `GET /api/CASAPRetreive`

Returns the in-process list of all AP transactions registered since the service last started. This is an in-memory store and does not persist across restarts.

**Response:** `200 OK` — array of `CASAPTransaction` objects.

---

#### `GET /api/CASAPRetreive/GetAllTransactionRecords`

Same as above but the response is wrapped in a `JsonResult`.

**Response:** `200 OK` — JSON array of `CASAPTransaction` objects.

---

#### `POST /api/CASAPRetreive/GetTransactionRecords`

Queries the CAS ORDS API for the current status of a specific invoice.

**Headers:**

| Header          | Required | Description                                           |
| --------------- | -------- | ----------------------------------------------------- |
| `Authorization` | Yes      | `Bearer <jwt>`                                        |
| `clientID`      | Yes      | CAS API client ID for CAS OAuth token acquisition     |
| `secret`        | Yes      | CAS API client secret for CAS OAuth token acquisition |

**Request body:** [`CASAPQuery`](#casapquery-model)

```json
{
  "invoiceNumber": "INV-2024-001",
  "supplierNumber": "123456",
  "supplierSiteNumber": "001"
}
```

**Process:**

1. Acquires a CAS OAuth token via `POST {CAS_API_URI}oauth/token` using `client_credentials`.
2. Sends `GET {CAS_API_URI}cfs/apinvoice/{invoiceNumber}/{supplierNumber}/{supplierSiteNumber}` with the bearer token.
3. Returns the raw CAS response JSON.

**Success response:** `200 OK` — CAS invoice record JSON, including `invoice_number`, `invoice_status`, `payment_status`, `payment_number`, `payment_date`.

**Error responses:**

| Condition                       | Response                                                                                                                                  |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| Invoice/Supplier/Site not found | `{ "invoice_number": "...", "invoice_status": "Not Found", "payment_status": "Not found", "payment_number": " ", "payment_date": " " }`   |
| Credential or other error       | `{ "invoice_number": "...", "invoice_status": "<error>", "payment_status": "Generic Error", "payment_number": " ", "payment_date": " " }` |

---

### CASAPTransaction

Base route: `api/CASAPTransaction`

#### `POST /api/CASAPTransaction`

Submits an AP invoice to the CAS ORDS API. This is the primary invoice creation endpoint.

**Headers:**

| Header          | Required | Description           |
| --------------- | -------- | --------------------- |
| `Authorization` | Yes      | `Bearer <jwt>`        |
| `clientID`      | Yes      | CAS API client ID     |
| `secret`        | Yes      | CAS API client secret |

**Request body:** [`CASAPTransaction`](#casaptransaction-model)

```json
{
  "operatingUnit": "UNIT001",
  "invoiceType": "Standard",
  "supplierName": "Acme Corp",
  "supplierNumber": "123456",
  "supplierSiteNumber": "001",
  "invoiceDate": "07-MAY-2026",
  "invoiceNumber": "INV-2024-001",
  "invoiceAmount": 1500.0,
  "payGroup": "STD",
  "dateInvoiceReceived": "07-MAY-2026",
  "remittanceCode": "01",
  "specialHandling": "N",
  "payAloneFlag": "N",
  "glDate": "07-MAY-2026",
  "invoiceBatchName": "BATCH-2024-001",
  "currencyCode": "CAD",
  "invoiceLineDetails": [
    {
      "invoiceLineNumber": 1,
      "lineCode": "DR",
      "invoiceLineAmount": 1500.0,
      "defaultDistributionAccount": "001.123.45678.0000.0000"
    }
  ]
}
```

**Process:**

1. Registers the transaction in the in-memory store.
2. Acquires a CAS OAuth token via `POST {CAS_API_URI}oauth/token`.
3. Serializes the `CASAPTransaction` to JSON and sends it to `POST {CAS_API_URI}cfs/apinvoice/`.
4. Returns the CAS response JSON.

**Success response:** `200 OK` — CAS response JSON (e.g., confirmation with `invoice_number` and status).

**Error responses:**

| Condition      | Response                                                                           |
| -------------- | ---------------------------------------------------------------------------------- |
| CAS HTTP error | `{ "invoice_number": "...", "CAS_Returned_Messages": "CAS Error <code>: <body>" }` |
| Any exception  | `{ "invoice_number": null, "CAS_Returned_Messages": "Generic Error: <message>" }`  |

---

#### `POST /api/CASAPTransaction/InsertCASAPTransaction`

Inserts an AP transaction into the in-memory store **only** — does not forward to CAS. Useful for testing or pre-loading state.

**Request body:** [`CASAPTransaction`](#casaptransaction-model)

**Success response:** `200 OK` — `CASAPTransactionRegistrationReply` with `RegistrationStatus: "Success"`.

**Error response:** HTTP status code matching the exception's `HResult`.

---

### CASSupplierRetreive

Base route: `api/CASSupplierRetreive`

#### `POST /api/CASSupplierRetreive/GetTransactionRecords`

Queries the CAS ORDS API for supplier information.

**Headers:**

| Header          | Required | Description           |
| --------------- | -------- | --------------------- |
| `Authorization` | Yes      | `Bearer <jwt>`        |
| `clientID`      | Yes      | CAS API client ID     |
| `secret`        | Yes      | CAS API client secret |

**Request body:** [`CASSupplierQuery`](#cassupplierquery-model)

```json
{
  "supplierNumber": "123456",
  "supplierSiteNumber": "001",
  "supplierName": "Acme Corp",
  "postalCode": "V8W 1N3"
}
```

**Process:**

1. Acquires a CAS OAuth token.
2. Sends `GET {CAS_API_URI}cfs/apinvoice/` with the bearer token.
3. Returns the raw CAS supplier response JSON.

**Success response:** `200 OK` — CAS supplier record JSON.

**Error response:** A constructed JSON object with all supplier fields set to `null` and `site_status: "Error: <message>"`.

---

### CornetTransaction

Base route: `api/CornetTransaction`

#### `POST /api/CornetTransaction`

Receives a CORNET event notification, stores it in the in-memory registry, and synchronously forwards it to Dynamics 365.

**Request body:** [`CornetTransaction`](#cornettransaction-model)

```json
{
  "event_message_id": 100001,
  "target_system_cd": "DYNAMICS",
  "message_event_type_cd": "NOTIFICATION",
  "event_dtm": "2026-05-07T12:00:00Z",
  "event_data": [
    { "data_element_nm": "case_id", "data_value_txt": "CASE-001" },
    { "data_element_nm": "status", "data_value_txt": "ACTIVE" },
    { "data_element_nm": "element3", "data_value_txt": "value3" },
    { "data_element_nm": "element4", "data_value_txt": "value4" }
  ]
}
```

> **Note:** Exactly **4 entries** are required in `event_data`. The mapping to Dynamics fields uses fixed array indices [0]–[3].

**Process:**

1. Registers the transaction in the in-memory `CornetTransactionRegistration` store.
2. Maps the `CornetTransaction` to a `CornetDynamicsModel` via `ToCornetDynamicsModel()`.
3. Acquires a Dynamics 365 OAuth token using `DynamicsAuthHelper`.
4. Posts to `{Dynamics:ApiEndpointUrl}vsd_CreateCORNETNotifications` via OData HTTP POST.
5. Evaluates the Dynamics response:
   - Response containing `"Cornet Notification "` → success.
   - Any other response → failure with the raw Dynamics result as the error code.

**Success response:** `200 OK`

```json
{ "ResponseCode": "200", "ResponseMessage": "Success" }
```

**Failure response:** `200 OK` (HTTP status is always 200; check `ResponseMessage`)

```json
{ "ResponseCode": "<dynamics-response>", "ResponseMessage": "Failure" }
```

---

#### `POST /api/CornetTransaction/InsertCornetTransaction`

Inserts a CORNET transaction into the in-memory store **only** — does not forward to Dynamics.

**Request body:** [`CornetTransaction`](#cornettransaction-model)

**Success response:** `200 OK` — `CornetTransactionRegistrationReply` with `ResponseMessage: "Success"`.

**Error response:** HTTP status code matching the exception's `HResult`.

---

## Data Models

### CASAPTransaction Model

Represents a full AP invoice payload sent to CAS.

| Field                   | Type    | Required | Max Length | Notes                                                    |
| ----------------------- | ------- | -------- | ---------- | -------------------------------------------------------- |
| `operatingUnit`         | string  | No       | —          | CAS operating unit code                                  |
| `invoiceType`           | string  | Yes      | 25         | e.g., `"Standard"`                                       |
| `poNumber`              | string  | No       | —          | Always serialized as `null`                              |
| `supplierName`          | string  | No       | —          | Supplier display name                                    |
| `supplierNumber`        | string  | Yes      | 30         | CAS supplier number                                      |
| `supplierSiteNumber`    | string  | Yes      | 3          | CAS supplier site code                                   |
| `invoiceDate`           | string  | Yes      | —          | Format: `DD-MON-YYYY` (e.g., `07-MAY-2026`)              |
| `invoiceNumber`         | string  | Yes      | 40         | Unique invoice identifier                                |
| `invoiceAmount`         | decimal | Yes      | —          | Format: `9(12).99`                                       |
| `payGroup`              | string  | Yes      | 7          | Payment group code                                       |
| `dateInvoiceReceived`   | string  | Yes      | —          | Format: `DD-MON-YYYY`                                    |
| `dateGoodsReceived`     | string  | No       | —          | Format: `DD-MON-YYYY`                                    |
| `remittanceCode`        | string  | Yes      | 2          | CAS remittance code                                      |
| `specialHandling`       | string  | Yes      | 1          | `"Y"` or `"N"`                                           |
| `bankNumber`            | string  | No       | 4          | EFT bank number                                          |
| `branchNumber`          | string  | No       | 5          | EFT branch (transit) number                              |
| `accountNumber`         | string  | No       | 12         | EFT account number                                       |
| `eftAdviceFlag`         | string  | No       | 1          | EFT advice preference flag                               |
| `eftEmailAddress`       | string  | No       | 35         | Email for EFT remittance advice                          |
| `nameLine1`             | string  | No       | 40         | Payee name line 1                                        |
| `nameLine2`             | string  | No       | 40         | Payee name line 2                                        |
| `addressLine1`          | string  | No       | 40         |                                                          |
| `addressLine2`          | string  | No       | 40         |                                                          |
| `addressLine3`          | string  | No       | 40         |                                                          |
| `terms`                 | string  | No       | —          | Payment terms                                            |
| `payAloneFlag`          | string  | Yes      | 1          | `"Y"` or `"N"`. Defaults to `"N"` when blank.            |
| `paymentAdviceComments` | string  | No       | 40         |                                                          |
| `remittanceMessage1`    | string  | No       | 150        |                                                          |
| `remittanceMessage2`    | string  | No       | 150        |                                                          |
| `remittanceMessage3`    | string  | No       | 150        |                                                          |
| `termsDate`             | string  | No       | —          | Format: `DD-MON-YYYY`                                    |
| `glDate`                | string  | Yes      | —          | GL effective date. Format: `DD-MON-YYYY`                 |
| `invoiceBatchName`      | string  | Yes      | 50         | Batch identifier                                         |
| `currencyCode`          | string  | Yes      | 3          | Always serialized as `"CAD"`                             |
| `invoiceLineDetails`    | array   | No       | —          | Array of [`InvoiceLineDetail`](#invoicelinedetail-model) |

---

### InvoiceLineDetail Model

Represents a single line item on an AP invoice.

| Field                        | Type    | Required | Max Length | Notes                                |
| ---------------------------- | ------- | -------- | ---------- | ------------------------------------ |
| `invoiceLineNumber`          | int     | Yes      | —          | Sequential line number starting at 1 |
| `invoiceLineType`            | string  | Yes      | 4          | Always serialized as `"Item"`        |
| `distributionTotal`          | string  | No       | —          | Always serialized as `null`          |
| `lineCode`                   | string  | Yes      | 2          | `"DR"` (debit) or `"CR"` (credit)    |
| `invoiceLineAmount`          | decimal | Yes      | —          | Format: `9(12).99`                   |
| `defaultDistributionAccount` | string  | Yes      | 40         | GL distribution account string       |
| `description`                | string  | No       | —          | Line item description                |
| `taxClassificationCode`      | string  | No       | 30         | GST/PST classification               |
| `distributionSupplier`       | string  | No       | 30         | Override supplier for this line      |
| `info1`                      | string  | No       | 25         |                                      |
| `info2`                      | string  | No       | 10         |                                      |
| `info3`                      | string  | No       | —          |                                      |

---

### CASAPQuery Model

Used to query CAS for a specific invoice.

| Field                | Type   | Required | Max Length |
| -------------------- | ------ | -------- | ---------- |
| `invoiceNumber`      | string | Yes      | 40         |
| `supplierNumber`     | string | Yes      | 40         |
| `supplierSiteNumber` | string | Yes      | 40         |

---

### CASSupplierQuery Model

Used to query CAS for supplier information.

| Field                | Type   | Required | Max Length |
| -------------------- | ------ | -------- | ---------- |
| `supplierNumber`     | string | No       | 40         |
| `supplierSiteNumber` | string | No       | 40         |
| `supplierName`       | string | No       | 40         |
| `postalCode`         | string | No       | 10         |

---

### CornetTransaction Model

Represents a CORNET system event notification.

| Field                   | Type     | Required | Notes                                                                                                                           |
| ----------------------- | -------- | -------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `event_message_id`      | int64    | Yes      | Unique message identifier from CORNET                                                                                           |
| `target_system_cd`      | string   | Yes      | Target system code (e.g., `"DYNAMICS"`)                                                                                         |
| `message_event_type_cd` | string   | Yes      | Event type code (e.g., `"NOTIFICATION"`)                                                                                        |
| `event_dtm`             | DateTime | Yes      | Event timestamp (ISO 8601)                                                                                                      |
| `event_data`            | array    | No       | Array of [`CornetTransactionEventData`](#cornettransactioneventdata-model). Exactly 4 entries required for Dynamics forwarding. |

---

### CornetTransactionEventData Model

Key–value pair describing one element of a CORNET event.

| Field             | Type   | Required |
| ----------------- | ------ | -------- |
| `data_element_nm` | string | Yes      |
| `data_value_txt`  | string | Yes      |

---

### CornetDynamicsModel

Internal model passed to Dynamics 365. Mapped from `CornetTransaction` via `ToCornetDynamicsModel()`.

| Field          | Type           | Mapped From                     |
| -------------- | -------------- | ------------------------------- |
| `EventId`      | int64          | `event_message_id`              |
| `EventType`    | string         | `message_event_type_cd`         |
| `EventDate`    | DateTimeOffset | `event_dtm`                     |
| `DataElement1` | string         | `event_data[0].data_element_nm` |
| `DataValue1`   | string         | `event_data[0].data_value_txt`  |
| `DataElement2` | string         | `event_data[1].data_element_nm` |
| `DataValue2`   | string         | `event_data[1].data_value_txt`  |
| `DataElement3` | string         | `event_data[2].data_element_nm` |
| `DataValue3`   | string         | `event_data[2].data_value_txt`  |
| `DataElement4` | string         | `event_data[3].data_element_nm` |
| `DataValue4`   | string         | `event_data[3].data_value_txt`  |

---

## CAS OAuth Token Flow

All CAS-facing controllers follow the same two-step OAuth pattern:

```
1. POST {CAS_API_URI}oauth/token
   Authorization: Basic Base64({clientID}:{secret})
   Body: grant_type=client_credentials

   → { "access_token": "<token>", ... }

2. GET/POST {CAS_API_URI}cfs/apinvoice/...
   Authorization: Bearer <access_token>
```

`clientID` and `secret` are passed by the **caller** in request headers, not stored in service configuration. This means the CAS credentials are supplied per-request by the upstream consumer.

---

## CORNET-to-Dynamics Integration

When `POST /api/CornetTransaction` is called:

1. The `CornetTransaction` is mapped to `CornetDynamicsModel` (fixed 4-element event data array).
2. `DynamicsAuthHelper.CreateTokenProvider(configuration, httpClientFactory)` selects the appropriate token provider based on the `Dynamics` configuration section (ADFS or Entra ID).
3. The token is acquired and used to POST to:
   ```
   {Dynamics:ApiEndpointUrl}vsd_CreateCORNETNotifications
   ```
   with OData headers (`OData-MaxVersion: 4.0`, `OData-Version: 4.0`, `Accept: application/json`).
4. The JSON `@odata.context` key in the response is deserialized into a `DynamicsResponseModel` to extract `IsSuccess` and `Result`.
5. A `"Cornet Notification "` prefix in the result text signals success.

---

## Logging

The service uses **Serilog** with two configurable sinks:

| Sink       | Condition                                          | Details                                                                                                    |
| ---------- | -------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| Console    | Always active                                      | All log events at `Debug` and above                                                                        |
| Splunk HEC | `SPLUNK_COLLECTOR_URL` and `SPLUNK_TOKEN` both set | Events at `Information` and above. Source: `cas-api`, SourceType: `coast:cas-api`, Host: machine hostname. |

In `Development` mode, Splunk's SSL certificate validation is disabled to support self-signed certificates.

Log enrichment properties applied to every event:

| Property      | Value             |
| ------------- | ----------------- |
| `ServiceName` | `cas-api`         |
| `ServiceType` | `coast-utilities` |

Request-level logging via `UseSerilogRequestLogging`:

- Health check requests (`/hc`) are logged at `Verbose` unless they return a 5xx error.
- Client errors (4xx) are logged at `Warning`.
- Server errors (5xx) and exceptions are logged at `Error`.
- All other requests are logged at `Information`.

JWT events logged per-request:

- Token validated → `Information` with `subject` claim.
- Authentication failed → `Warning` with exception message.

---

## Deployment

### Docker

The multi-stage Dockerfile builds a minimal Alpine-based image:

```
Stage 1 (build):  mcr.microsoft.com/dotnet/sdk:10.0
Stage 2 (final):  mcr.microsoft.com/dotnet/aspnet:10.0-alpine
```

Build arguments:

| Arg             | Written to                                     |
| --------------- | ---------------------------------------------- |
| `BUILD_ID`      | `/app/build_id.txt` and `BUILD_ID` env var     |
| `BUILD_VERSION` | `/app/version.txt` and `BUILD_VERSION` env var |

The container listens on **port 8080** (`ASPNETCORE_URLS=http://*:8080`).

### OpenShift

| Setting        | Value                                                       |
| -------------- | ----------------------------------------------------------- |
| Namespace      | `pssg-cscp-vsd`                                             |
| Component name | `cas-interface-service`                                     |
| Git URI        | `https://github.com/JonTaylorBCGov/CASInterfaceService.git` |
| Git ref        | `master`                                                    |

OpenShift templates are located in:

- `CASInterfaceService/openshift/templates/`
- `aem-interface-service/openshift/templates/`

---

## Local Development

### Prerequisites

- .NET 10 SDK
- Access to a Keycloak realm (or disable auth with `auth:jwt:enabled=false`)
- CAS ORDS API credentials (or a mock/stub)
- Dynamics 365 credentials (for CORNET flow)

### Running locally

```bash
cd CASInterfaceService/CASInterfaceService
dotnet run
```

The service starts on `http://localhost:8080`.

### User Secrets (recommended for credentials)

```bash
dotnet user-secrets set "CAS_API_URI" "https://<server>:<port>/ords/casords/"
dotnet user-secrets set "auth:jwt:authority" "https://<keycloak>/auth/realms/standard"
dotnet user-secrets set "auth:jwt:audience" "<audience>"
dotnet user-secrets set "auth:jwt:enabled" "false"
```

### Disabling authentication locally

Set `auth:jwt:enabled` to `false` in `appsettings.Development.json` or via user secrets. All API requests will be permitted without a JWT.

---

## Postman Collection

A Postman collection is included at `postman/postman.json` with the following collection variables:

| Variable       | Default                                                               | Description                                         |
| -------------- | --------------------------------------------------------------------- | --------------------------------------------------- |
| `baseUrl`      | `http://localhost:8080`                                               | Service base URL                                    |
| `tokenUrl`     | `<bcgov_keycloak>/auth/realms/standard/protocol/openid-connect/token` | Keycloak token endpoint                             |
| `clientId`     | `<resource>`                                                          | Keycloak client ID                                  |
| `clientSecret` | `<credentials.secret>`                                                | Keycloak client secret                              |
| `accessToken`  | _(auto-set)_                                                          | Populated by the "Get JWT Token" pre-request script |

Environment-specific variable files are also provided:

| File                                 | Environment        |
| ------------------------------------ | ------------------ |
| `postman.CAS-LOCAL.environment.json` | Local development  |
| `postman.CAS-DEV.environment.json`   | Dev/test OpenShift |
| `postman.CAS-TEST.environment.json`  | Test OpenShift     |

### Collection structure

```
Auth
└── Get JWT Token  (POST {{tokenUrl}})

CASAPRetreive
├── Get All Transactions            GET  /api/CASAPRetreive
├── Get All Transaction Records     GET  /api/CASAPRetreive/GetAllTransactionRecords
└── Get Transaction Records         POST /api/CASAPRetreive/GetTransactionRecords

CASAPTransaction
├── Register CAS AP Transaction     POST /api/CASAPTransaction
└── Insert CAS AP Transaction       POST /api/CASAPTransaction/InsertCASAPTransaction

CASSupplierRetreive
└── Get Supplier Transaction Records POST /api/CASSupplierRetreive/GetTransactionRecords

CornetTransaction
├── Register Cornet Transaction     POST /api/CornetTransaction
└── Insert Cornet Transaction       POST /api/CornetTransaction/InsertCornetTransaction
```
