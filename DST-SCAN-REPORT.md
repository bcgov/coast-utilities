# DST Impact Scan — COAST OpenShift Applications

**Scan date:** 2026-08-11  
**Scope:** All services in `coast-utilities` mono-repo  
**Services reviewed:** AEMInterfaceService · CASInterfaceService · CornetInterfaceService · job-scheduling · Shared.Database

---

## Executive Summary

All four COAST services run in **UTC-only containers** (Alpine/Debian Linux, no `TZ` environment variable set). This baseline eliminates the most common class of DST bugs. Three confirmed issues were found and remediated. One residual risk requires an operator action outside this repository.

---

## Scan Areas Covered

| Area                                    | Reviewed               |
| --------------------------------------- | ---------------------- |
| Application C# source code              | ✅                     |
| OpenShift deploy templates (JSON)       | ✅                     |
| Dockerfiles                             | ✅                     |
| GitHub Actions CI/CD workflows          | ✅                     |
| `appsettings.json` / environment config | ✅                     |
| NuGet package manifest (`.csproj`)      | ✅                     |
| Shared.Database token caching           | ✅                     |
| OpenShift CronJob schedule              | ✅ (see Residual Risk) |

---

## Findings and Remediations

### FINDING-01 — HIGH — `CornetTransaction.event_dtm` typed as `DateTime` ✅ FIXED

**Files affected (before fix):**

- `AEMInterfaceService/AEMInterfaceService/Pages/Models/CornetTransaction.cs`
- `CASInterfaceService/CASInterfaceService/Pages/Models/CornetTransaction.cs`
- `CornetInterfaceService/CornetInterfaceService/Pages/Models/CornetTransaction.cs`

**Root cause:**  
The `event_dtm` field was declared as `DateTime` (timezone-unaware). When a caller sends a timestamp without an offset (e.g., `"2024-03-10T02:30:00"`), the JSON deserializer sets `DateTimeKind.Unspecified`. The subsequent assignment to `CornetDynamicsModel.EventDate` (which is `DateTimeOffset`) implicitly applied the process's local UTC offset — masking the ambiguity. During a DST transition the "spring forward" hour does not exist and the "fall back" hour is ambiguous, making an `Unspecified` kind incorrect.

**Remediation applied:**  
Changed `DateTime EventDTM` → `DateTimeOffset EventDTM` (and the public property type) in all three `CornetTransaction.cs` files. `DateTimeOffset` always carries an explicit UTC offset, making the DST ambiguity impossible to introduce silently. The outbound `CornetDynamicsModel.EventDate` was already `DateTimeOffset`; the intermediate model now matches it.

**Build verification:** All four projects built with 0 errors after the change.

---

### FINDING-02 — MEDIUM — Alpine images lacked `tzdata` package ✅ FIXED

**Files affected (before fix):**

- `AEMInterfaceService/AEMInterfaceService/Dockerfile`
- `CASInterfaceService/CASInterfaceService/Dockerfile`
- `CornetInterfaceService/Dockerfile`

**Root cause:**  
`mcr.microsoft.com/dotnet/aspnet:10.0-alpine` does not include the `tzdata` package. Any future call to `TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver")` or similar IANA-name lookups would throw `TimeZoneNotFoundException` at runtime. The current code does not use named timezone IDs, but this is a latent risk whenever timezone conversion logic is added.

`job-scheduling` uses `mcr.microsoft.com/dotnet/runtime:10.0` (Debian-based), which ships `tzdata` by default — no change needed there.

**Remediation applied:**  
Added `RUN apk add --no-cache tzdata` to all three Alpine-based Dockerfiles immediately after the `adduser` step in the final stage.

---

### FINDING-03 — LOW — `DateTime.Now` used for console log timestamps ✅ FIXED

**Files affected (before fix):**

- `AEMInterfaceService/.../AEMTransactionController.cs` (28 occurrences)
- `AEMInterfaceService/.../CASAPRetrieveController.cs`
- `AEMInterfaceService/.../CASAPTransactionController.cs`
- `AEMInterfaceService/.../CornetTransactionController.cs`
- `CornetInterfaceService/.../CASAPRetrieveController.cs`
- `CornetInterfaceService/.../CASAPTransactionController.cs`
- `CornetInterfaceService/.../CornetTransactionController.cs`

**Root cause:**  
`DateTime.Now` returns local system time. Because no `TZ` environment variable is set on these containers, their local time is already UTC and `DateTime.Now == DateTime.UtcNow` in practice. However, the code's _intent_ is to log a timestamp: it should explicitly request UTC. If `TZ` were ever set to `America/Vancouver`, all log timestamps would shift by one hour at every DST boundary.

**Remediation applied:**  
Global replacement of `DateTime.Now` → `DateTime.UtcNow` across all affected source files. Log timestamps now explicitly read from the UTC clock and are DST-immune regardless of any future `TZ` configuration.

---

## Items Confirmed Unaffected

| Item                                                                  | Reason                                                                          |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| `CornetDynamicsModel.EventDate`                                       | Already `DateTimeOffset` in all three services — correct                        |
| `CASAPTransaction` date string fields (`invoiceDate`, `glDate`, etc.) | Date-only strings in format `"21-FEB-2017"` — no time component, DST-irrelevant |
| `Shared.Database` token cache (`TimeSpan.FromMinutes(5)`)             | Uses `TimeSpan` (duration), not wall-clock time — DST-immune                    |
| `appsettings.json` (all services)                                     | No timezone, schedule, or local-time references                                 |
| GitHub Actions CI `cron: "0 3 1,15 * *"`                              | Runs at 03:00 UTC for security scans; no user-facing timing requirement         |
| `job-scheduling` Dockerfile                                           | Debian base image already includes `tzdata`                                     |
| Third-party timezone libraries                                        | None present (no NodaTime, TimeZoneConverter, etc.)                             |
| JWT token validation (`ValidateLifetime = true`)                      | Uses UTC token expiry claims — unaffected by DST                                |

---

## Residual Risk — REQUIRES OPERATOR ACTION

### RISK-01 — MEDIUM — OpenShift CronJob schedule timezone not confirmed

The `promote-job-scheduling-to-prod.yml` workflow describes `job-scheduling` as an **OpenShift CronJob**. The OpenShift CronJob manifest is **not present in this repository**; it is deployed separately to the cluster.

**Risk:** By default, Kubernetes/OpenShift CronJob schedules are evaluated in **UTC**. If the job is intended to run at a specific Pacific local time (e.g., 08:00 PST / 07:00 PDT), the cron expression must either:

1. Be adjusted manually twice a year when clocks change, **or**
2. Use the `timeZone: America/Vancouver` field on the CronJob spec (available in Kubernetes ≥ 1.25 / OpenShift ≥ 4.12).

**Action required:** Audit the live OpenShift CronJob definition (e.g., `oc get cronjob job-scheduling -o yaml`) and confirm whether a `timeZone:` field is set. If the scheduled firing time must track Pacific local time, add `timeZone: America/Vancouver` to the CronJob spec.

#### Effect of BC permanently eliminating DST

If BC adopts a **permanent fixed UTC offset** (either UTC-8 or UTC-7 year-round), RISK-01 requires a **one-time correction** and then becomes permanently simpler:

- Determine the permanent offset BC adopts (UTC-8 = permanent PST, or UTC-7 = permanent PDT).
- Update the cron expression to the single fixed UTC equivalent of the desired Pacific fire time. For example, a job targeting 08:00 Pacific needs `0 16 * * *` for permanent UTC-8, or `0 15 * * *` for permanent UTC-7.
- After that correction, no further twice-yearly adjustments are ever needed.
- The `timeZone: America/Vancouver` field may be left in place (it will remain valid once `tzdata` is updated) or removed in favour of the hardcoded UTC expression.

**Additional requirement — tzdata currency:**  
The `tzdata` package installed in the Alpine images (added by FINDING-02) must be **current enough to include BC's new permanent-offset rule** for `America/Vancouver`. An image built before the updated `tzdata` is published will still apply the old DST transition rules. A forced image rebuild must be triggered on or after the date the updated `tzdata` package is released with BC's rule change.

---

## Testing Guidance

To verify correct processing across the DST transition boundary:

1. **`event_dtm` field (FINDING-01):** POST a `CornetTransaction` payload with each of the following timestamp formats and confirm the `EventDate` in the Dynamics write carries the correct UTC offset:
   - `"2024-03-10T02:30:00"` (no offset — now deserialises as `DateTimeOffset` with `+00:00`)
   - `"2024-03-10T02:30:00-08:00"` (explicit PST offset)
   - `"2024-03-10T09:30:00Z"` (UTC)

2. **Container timezone (`tzdata`):** After rebuilding the Alpine images, run:

   ```
   dotnet -e 'Console.WriteLine(TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver").DisplayName);'
   ```

   inside the container. It should print the Pacific timezone display name without exception.

3. **Log timestamps (FINDING-03):** Trigger a controller action and confirm log lines contain UTC timestamps (ending in `Z` or matching UTC wall clock).

4. **CronJob schedule (RISK-01):** Run the job manually at 01:59 UTC and 02:01 UTC on the second Sunday of March (spring-forward day). Verify it fires as expected and the Dynamics job executes without error.

---

## Summary Table

| ID         | Severity | Description                                       | Status                                                  |
| ---------- | -------- | ------------------------------------------------- | ------------------------------------------------------- |
| FINDING-01 | HIGH     | `CornetTransaction.event_dtm` typed as `DateTime` | ✅ Fixed — changed to `DateTimeOffset` in 3 files       |
| FINDING-02 | MEDIUM   | Alpine Dockerfiles missing `tzdata`               | ✅ Fixed — `apk add tzdata` added to 3 Dockerfiles      |
| FINDING-03 | LOW      | `DateTime.Now` in log statements                  | ✅ Fixed — replaced with `DateTime.UtcNow` in 7 files   |
| RISK-01    | MEDIUM   | OpenShift CronJob `timeZone:` not confirmed       | ⚠️ Open — operator must audit live cluster CronJob spec |
